using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryCategories;

/// <summary>
/// GET /api/v1/inventory/categories — les catégories de l'école courante, et elles seules
/// (Global Query Filter + policy RLS, comme GetBuildingsWithRoomsQuery). Aucun paramètre : une école
/// n'a pas assez de familles de biens pour justifier une pagination.
/// </summary>
public record GetInventoryCategoriesQuery : IRequest<IReadOnlyList<InventoryCategoryDto>>;

/// <summary><see cref="RowVersion"/> est le jeton xmin nécessaire à la modification/suppression.</summary>
public record InventoryCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    int ItemCount,
    int TotalQuantity,
    uint RowVersion);
