using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Buildings.Deletion;
using MediatR;

namespace SamaEcole.Application.Buildings.Commands.UnlinkBuildingDependencies;

/// <summary>
/// Rupture EXPLICITE des liens non financiers d'un bâtiment (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est
/// DeleteBuildingCommand, qui ne rompt jamais rien. Chaque lien est archivé ou détaché (rien n'est effacé).
/// IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkBuildingDependenciesCommand(Guid BuildingId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkBuildingDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkBuildingDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkBuildingDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        return await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, BuildingDeletionPolicy.Policy, request.BuildingId, actorId.ToString(), ct),
            cancellationToken);
    }
}
