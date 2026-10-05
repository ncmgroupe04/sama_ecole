using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.ReturnStudentUniform;

public class ReturnStudentUniformCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<ReturnStudentUniformCommand, StudentUniformDto>
{
    public async Task<StudentUniformDto> Handle(ReturnStudentUniformCommand request, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var line = await dbContext.StudentUniforms.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dotation {request.Id} introuvable.");

        // 409 si la ligne n'est pas « remise », 422 sans motif : avant de toucher au stock.
        var reason = UniformDeliveryPolicy.ValidateReturn(line.Status, request.Reason);

        // Le lot est retrouvé par le MOUVEMENT de remise, pas par la taille : archiver la taille après coup n'empêche
        // pas de reprendre une tenue déjà remise. Remise ⇒ mouvement non nul (contrainte CHECK en base).
        var delivery = await dbContext.StockMovements.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == line.DeliveryStockMovementId, cancellationToken)
            ?? throw new BusinessRuleException(
                "Le mouvement de stock de la remise est introuvable : le retour ne peut pas être enregistré.",
                "UNIFORM_DELIVERY_MOVEMENT_MISSING");

        var lot = await dbContext.InventoryItems.FirstOrDefaultAsync(l => l.Id == delivery.ItemId, cancellationToken)
            ?? throw new BusinessRuleException(
                "Le lot de stock de cette tenue a été archivé : restaurez-le depuis l'Inventaire avant d'enregistrer le retour.",
                "UNIFORM_LOT_UNAVAILABLE");

        dbContext.SetOriginalConcurrencyToken(line, request.RowVersion);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Mouvement inverse de la remise : Entrée (total et disponible remontent). Le journal est append-only, une
        // remise erronée se corrige par un mouvement inverse, jamais par une rature.
        var movement = StockLedger.Apply(
            lot,
            StockMovementType.Entree,
            line.Quantity,
            today,
            UniformStockLabels.Return(line.ItemLabel ?? "Tenue", line.SizeLabel ?? "", reason),
            UniformStockLabels.Counterparty(line.StudentLabel));

        dbContext.StockMovements.Add(movement);

        line.Status = StudentUniformStatus.Returned;
        line.ReturnStockMovementId = movement.Id;
        line.ReturnReason = reason;

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await StudentUniformReader.GetByIdsAsync(dbContext, [line.Id], cancellationToken)).Single();
    }
}
