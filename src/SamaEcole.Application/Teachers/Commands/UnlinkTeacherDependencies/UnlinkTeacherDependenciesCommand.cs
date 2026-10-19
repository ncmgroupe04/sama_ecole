using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Teachers.Deletion;
using MediatR;

namespace SamaEcole.Application.Teachers.Commands.UnlinkTeacherDependencies;

/// <summary>
/// POST /teachers/{id}/unlink-dependencies — rompt, sur demande EXPLICITE de l'utilisateur, les liens non financiers
/// d'enseignant (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est DeleteTeacherCommand, qui ne rompt jamais rien.
/// Chaque lien est archivé (suppression logique, auteur et date conservés). IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkTeacherDependenciesCommand(Guid TeacherId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkTeacherDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkTeacherDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkTeacherDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var impact = await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, TeacherDeletionPolicy.Policy, request.TeacherId, actorId.ToString(), ct),
            cancellationToken);

        return impact;
    }
}
