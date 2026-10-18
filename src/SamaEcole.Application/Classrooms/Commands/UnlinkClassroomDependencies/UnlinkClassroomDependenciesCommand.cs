using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Classrooms.Deletion;
using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.UnlinkClassroomDependencies;

/// <summary>
/// POST /classrooms/{id}/unlink-dependencies — rompt, sur demande EXPLICITE de l'utilisateur, les liens non financiers
/// d'classe (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est DeleteClassroomCommand, qui ne rompt jamais rien.
/// Chaque lien est archivé (suppression logique, auteur et date conservés). IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkClassroomDependenciesCommand(Guid ClassroomId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkClassroomDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IKpiCacheService kpiCache)
    : IRequestHandler<UnlinkClassroomDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkClassroomDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var impact = await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, ClassroomDeletionPolicy.Policy, request.ClassroomId, actorId.ToString(), ct),
            cancellationToken);

        // Après commit : l'occupation des classes et les effectifs ont changé.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return impact;
    }
}
