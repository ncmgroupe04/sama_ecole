using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Deletion;
using MediatR;

namespace SamaEcole.Application.Inventory.Commands.UnlinkInventoryCategoryDependencies;

/// <summary>
/// Rupture EXPLICITE des liens non financiers d'une catégorie d'inventaire (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est
/// DeleteInventoryCategoryCommand, qui ne rompt jamais rien. Chaque lien est archivé ou détaché (rien n'est effacé).
/// IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkInventoryCategoryDependenciesCommand(Guid InventoryCategoryId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkInventoryCategoryDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkInventoryCategoryDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkInventoryCategoryDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        return await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, InventoryCategoryDeletionPolicy.Policy, request.InventoryCategoryId, actorId.ToString(), ct),
            cancellationToken);
    }
}
