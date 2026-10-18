using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Deletion;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.DeleteInventoryCategory;

public class DeleteInventoryCategoryCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteInventoryCategoryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteInventoryCategoryCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var category = await dbContext.InventoryCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Catégorie d'inventaire {request.Id} introuvable.");

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Une donnée financière active la refuse (texte
        // imposé) ; tout autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkInventoryCategoryDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, InventoryCategoryDeletionPolicy.Policy, request.Id, cancellationToken);

        dbContext.SetOriginalConcurrencyToken(category, request.RowVersion);

        category.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
