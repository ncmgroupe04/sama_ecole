using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Deletion;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.DeleteFeeCategory;

public class DeleteFeeCategoryCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteFeeCategoryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser une
        // catégorie d'une autre école renvoie 404, jamais une suppression silencieuse.
        var category = await dbContext.FeeCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Catégorie de frais {request.Id} introuvable.");

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Une donnée financière active la refuse (texte
        // imposé) ; tout autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkFeeCategoryDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, FeeCategoryDeletionPolicy.Policy, request.Id, cancellationToken);

        category.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
