using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Deletion;
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

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Une donnée financière active la refuse (texte
        // imposé) ; tout autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkInventoryItemDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, InventoryItemDeletionPolicy.Policy, request.Id, cancellationToken);

        dbContext.SetOriginalConcurrencyToken(item, request.RowVersion);

        // Les prêts clos font partie de l'historique du bien : archivés avec lui (annoncé à l'écran).
        await EntityDeletionAnalyzer.ArchiveIntrinsicAsync(
            dbContext, InventoryItemDeletionPolicy.Policy, request.Id, actorId.ToString(), cancellationToken);

        // Le journal de stock, lui, n'est PAS effacé : il est append-only (AGENTS.md règle #6 poussée
        // à son terme). L'historique du lot reste consultable après archivage de sa fiche.
        item.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
