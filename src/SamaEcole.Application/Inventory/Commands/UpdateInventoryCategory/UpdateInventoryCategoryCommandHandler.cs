using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Commands.CreateInventoryCategory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.UpdateInventoryCategory;

public class UpdateInventoryCategoryCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateInventoryCategoryCommand, InventoryCategoryResult>
{
    public async Task<InventoryCategoryResult> Handle(
        UpdateInventoryCategoryCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une catégorie d'une autre école renvoie 404, jamais une modification silencieuse.
        var category = await dbContext.InventoryCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Catégorie d'inventaire {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(category, request.RowVersion);

        category.Name = request.Name.Trim();
        category.Description = request.Description?.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.InventoryCategories.AsNoTracking()
            .Where(c => c.Id == category.Id)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .FirstAsync(cancellationToken);

        return new InventoryCategoryResult(category.Id, category.Name, category.Description, newRowVersion);
    }
}
