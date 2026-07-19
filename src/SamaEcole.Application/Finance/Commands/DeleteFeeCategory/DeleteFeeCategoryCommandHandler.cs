using SamaEcole.Application.Common.Interfaces;
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

        var classFees = await dbContext.ClassFees
            .Where(f => f.FeeCategoryId == request.Id)
            .ToListAsync(cancellationToken);

        var actorIdText = actorId.ToString();

        // Une seule transaction : soit la catégorie ET tout son barème disparaissent ensemble, soit
        // rien — un échec au milieu ne doit pas laisser une catégorie supprimée avec des lignes de
        // barème orphelines encore visibles (voir DeleteFeeCategoryCommand pour le raisonnement).
        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var fee in classFees)
            {
                fee.SoftDelete(actorIdText);
            }

            category.SoftDelete(actorIdText);

            await dbContext.SaveChangesAsync(ct);
            return 0;
        }, cancellationToken);

        return Unit.Value;
    }
}
