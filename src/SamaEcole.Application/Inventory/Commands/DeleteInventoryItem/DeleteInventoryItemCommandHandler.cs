using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.DeleteInventoryItem;

public class DeleteInventoryItemCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteInventoryItemCommand, Unit>
{
    public async Task<Unit> Handle(DeleteInventoryItemCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var item = await dbContext.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Bien d'inventaire {request.Id} introuvable.");

        // Archiver un lot dont des unités sont dehors ferait disparaître la trace du prêt : la
        // décharge signée par le bénéficiaire n'aurait plus de bien en face.
        var hasOpenAssignment = await dbContext.ItemAssignments.AnyAsync(
            a => a.ItemId == item.Id
                 && (a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue),
            cancellationToken);

        if (hasOpenAssignment)
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : des prêts de ce bien sont encore en cours.");
        }

        dbContext.SetOriginalConcurrencyToken(item, request.RowVersion);

        // Le journal de stock, lui, n'est PAS effacé : il est append-only (AGENTS.md règle #6 poussée
        // à son terme). L'historique du lot reste consultable après archivage de sa fiche.
        item.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
