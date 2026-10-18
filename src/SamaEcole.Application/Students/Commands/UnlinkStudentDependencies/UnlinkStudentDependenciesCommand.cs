using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students.Deletion;
using MediatR;

namespace SamaEcole.Application.Students.Commands.UnlinkStudentDependencies;

/// <summary>
/// POST /students/{id}/unlink-dependencies — rompt, sur demande EXPLICITE de l'utilisateur, les liens non financiers
/// d'un élève (spec 2026-10-17, §2.1). Ne supprime PAS la fiche : c'est DeleteStudentCommand, qui ne rompt jamais rien.
/// Chaque lien est archivé (suppression logique, auteur et date conservés). IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkStudentDependenciesCommand(Guid StudentId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkStudentDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IKpiCacheService kpiCache)
    : IRequestHandler<UnlinkStudentDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkStudentDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var impact = await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, StudentDeletionPolicy.Policy, request.StudentId, actorId.ToString(), ct),
            cancellationToken);

        // Après commit : les effectifs et l'occupation des classes ont changé.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return impact;
    }
}
