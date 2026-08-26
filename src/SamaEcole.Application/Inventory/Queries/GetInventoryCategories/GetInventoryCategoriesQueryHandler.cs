using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryCategories;

public class GetInventoryCategoriesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetInventoryCategoriesQuery, IReadOnlyList<InventoryCategoryDto>>
{
    public async Task<IReadOnlyList<InventoryCategoryDto>> Handle(
        GetInventoryCategoriesQuery request, CancellationToken cancellationToken)
    {
        // ToListOrEmptyOnMissingTableAsync (et non ToListAsync) : module récent, dont la migration peut
        // ne pas encore être appliquée sur certains environnements — l'écran d'accueil du module doit
        // alors afficher son état vide normal, jamais un 500 brut (même choix que GetBuildingsWithRooms).
        var categories = await dbContext.ToListOrEmptyOnMissingTableAsync(
            dbContext.InventoryCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new InventoryCategoryDto(
                    c.Id,
                    c.Name,
                    c.Description,
                    dbContext.InventoryItems.Count(i => i.CategoryId == c.Id),
                    dbContext.InventoryItems.Where(i => i.CategoryId == c.Id).Sum(i => (int?)i.QuantityTotal) ?? 0,
                    EF.Property<uint>(c, "xmin"))),
            cancellationToken);

        return categories;
    }
}
