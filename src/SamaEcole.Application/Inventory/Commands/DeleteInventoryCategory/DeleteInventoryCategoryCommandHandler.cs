using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
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

        // Même règle que DeleteBuildingCommandHandler vis-à-vis des salles : une catégorie encore
        // porteuse de biens ne s'archive pas, sinon la fiche d'inventaire listerait des lots rattachés
        // à une famille disparue.
        var hasItems = await dbContext.InventoryItems
            .AnyAsync(i => i.CategoryId == request.Id, cancellationToken);

        if (hasItems)
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : des biens sont encore rattachés à cette catégorie.");
        }

        dbContext.SetOriginalConcurrencyToken(category, request.RowVersion);

        category.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
