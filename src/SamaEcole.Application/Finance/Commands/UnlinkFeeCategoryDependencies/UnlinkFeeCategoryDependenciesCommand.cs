using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Deletion;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.UnlinkFeeCategoryDependencies;

/// <summary>
/// Rupture EXPLICITE des liens non financiers d'une catégorie de frais (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est
/// DeleteFeeCategoryCommand, qui ne rompt jamais rien. Chaque lien est archivé ou détaché (rien n'est effacé).
/// IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkFeeCategoryDependenciesCommand(Guid FeeCategoryId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkFeeCategoryDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkFeeCategoryDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkFeeCategoryDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        return await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, FeeCategoryDeletionPolicy.Policy, request.FeeCategoryId, actorId.ToString(), ct),
            cancellationToken);
    }
}
