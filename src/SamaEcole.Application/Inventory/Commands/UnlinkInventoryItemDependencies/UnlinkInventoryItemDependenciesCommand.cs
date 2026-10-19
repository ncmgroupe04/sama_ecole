using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Deletion;
using MediatR;

namespace SamaEcole.Application.Inventory.Commands.UnlinkInventoryItemDependencies;

/// <summary>
/// Rupture EXPLICITE des liens non financiers d'un bien d'inventaire (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est
/// DeleteInventoryItemCommand, qui ne rompt jamais rien. Chaque lien est archivé ou détaché (rien n'est effacé).
/// IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkInventoryItemDependenciesCommand(Guid InventoryItemId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkInventoryItemDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkInventoryItemDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkInventoryItemDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        return await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, InventoryItemDeletionPolicy.Policy, request.InventoryItemId, actorId.ToString(), ct),
            cancellationToken);
    }
}
