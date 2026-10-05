using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.DeliverStudentUniform;

public class DeliverStudentUniformCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<DeliverStudentUniformCommand, StudentUniformDto>
{
    public async Task<StudentUniformDto> Handle(DeliverStudentUniformCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var line = await dbContext.StudentUniforms.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dotation {request.Id} introuvable.");

        // L'état d'abord, avant toute autre lecture : une tenue déjà remise ne se remet pas.
        UniformDeliveryPolicy.EnsureDeliverable(line.Status);

        var item = await dbContext.UniformItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == line.UniformItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Modèle de tenue {line.UniformItemId} introuvable.");

        // Facturation : LUE dans les lignes de frais de l'inscription, jamais écrite (règle #4). Calculée à la remise
        // puis figée sur la ligne — un barème modifié plus tard ne réécrit pas l'historique.
        var feeLines = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == line.EnrollmentId)
            .Select(l => new { l.Id, l.FeeCategoryId })
            .ToListAsync(cancellationToken);

        var billing = UniformBillingCheck.Evaluate(
            item.FeeCategoryId, item.QuantityIncluded, line.Quantity, feeLines.Select(l => l.FeeCategoryId).ToHashSet());

        // 422 : taille ou motif manquant. Rien n'est encore écrit, ni stock ni ligne.
        var notBilledReason = UniformDeliveryPolicy.ValidateDelivery(
            line.Status, line.UniformVariantId, line.Quantity, billing, request.NotBilledReason);

        var variant = await dbContext.UniformVariants.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == line.UniformVariantId && v.UniformItemId == line.UniformItemId, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure(
                "uniformVariantId", "La taille de cette ligne n'existe plus : choisissez-en une autre avant la remise.")]);

        // Le lot est SUIVI : EF pose son jeton xmin dans le WHERE de l'UPDATE. Si un autre guichet a sorti l'unité
        // entre cette lecture et l'écriture, SaveChangesAsync lève ConcurrencyConflictException (409) — jamais un stock
        // négatif ni une remise à moitié écrite.
        var lot = await dbContext.InventoryItems.FirstOrDefaultAsync(l => l.Id == variant.InventoryItemId, cancellationToken)
            ?? throw new BusinessRuleException(
                "Le lot de stock de cette taille a été archivé : restaurez-le depuis l'Inventaire avant la remise.",
                "UNIFORM_LOT_UNAVAILABLE");

        dbContext.SetOriginalConcurrencyToken(line, request.RowVersion);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // StockLedger est l'UNIQUE chemin d'écriture des compteurs : il lève 422 si le disponible est insuffisant, et
        // produit la ligne de journal (instantanés compris). Sortie, pas Attribution : la tenue ne revient pas.
        var movement = StockLedger.Apply(
            lot,
            StockMovementType.Sortie,
            line.Quantity,
            today,
            UniformStockLabels.Delivery(item.Name, variant.SizeLabel, line.StudentLabel),
            UniformStockLabels.Counterparty(line.StudentLabel));

        dbContext.StockMovements.Add(movement);

        line.Status = StudentUniformStatus.Delivered;
        line.DeliveredOn = today;
        line.DeliveredByUserId = actorId;
        line.DeliveryStockMovementId = movement.Id;
        line.BillingStatus = billing;
        line.FeeLineId = billing == UniformBillingStatus.Billed
            ? feeLines.First(l => l.FeeCategoryId == item.FeeCategoryId).Id
            : null;
        line.NotBilledReason = notBilledReason;

        // Libellés FIGÉS : la fiche réimprimée plus tard doit rester identique à celle qui a été signée.
        line.ItemLabel = item.Name;
        line.SizeLabel = variant.SizeLabel;

        // Une seule sauvegarde = une seule transaction : ligne, mouvement et compteurs du lot passent ensemble ou pas du tout.
        await dbContext.SaveChangesAsync(cancellationToken);

        return (await StudentUniformReader.GetByIdsAsync(dbContext, [line.Id], cancellationToken)).Single();
    }
}
