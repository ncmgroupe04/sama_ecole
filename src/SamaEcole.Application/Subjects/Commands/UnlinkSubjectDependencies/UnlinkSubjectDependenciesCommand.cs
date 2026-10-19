using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subjects.Deletion;
using MediatR;

namespace SamaEcole.Application.Subjects.Commands.UnlinkSubjectDependencies;

/// <summary>
/// POST /subjects/{id}/unlink-dependencies — rompt, sur demande EXPLICITE de l'utilisateur, les liens non financiers
/// d'matière (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est DeleteSubjectCommand, qui ne rompt jamais rien.
/// Chaque lien est archivé (suppression logique, auteur et date conservés). IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkSubjectDependenciesCommand(Guid SubjectId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkSubjectDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkSubjectDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkSubjectDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var impact = await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, SubjectDeletionPolicy.Policy, request.SubjectId, actorId.ToString(), ct),
            cancellationToken);

        return impact;
    }
}
