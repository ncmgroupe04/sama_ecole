using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryItems;

/// <summary>
/// GET /api/v1/inventory/items — catalogue paginé du patrimoine, filtrable. Pagination selon la
/// convention transverse (docs/Volume_4_API_Design.md §0.2 : page/pageSize, 20 par défaut, 100 max).
/// </summary>
public record GetInventoryItemsQuery : IRequest<PaginatedInventoryItems>
{
    public Guid? CategoryId { get; init; }

    public Guid? RoomId { get; init; }

    public ItemCondition? Condition { get; init; }

    /// <summary>Recherche insensible à la casse sur le libellé ET le code d'inventaire.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Ne garder que les lots dont il ne reste RIEN de disponible. Nommé sans ambiguïté : le modèle ne
    /// porte aucun seuil de réapprovisionnement, un filtre « stock bas » laisserait croire le contraire.
    /// </summary>
    public bool OutOfStockOnly { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public record PaginatedInventoryItems(
    IReadOnlyList<InventoryItemListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// <see cref="OnLoanQuantity"/> est calculé (total - disponible) plutôt que stocké : une colonne de
/// plus à tenir en cohérence pour une soustraction que la base fait gratuitement.
/// </summary>
public record InventoryItemListItem(
    Guid Id,
    string Name,
    string? Code,
    Guid CategoryId,
    string CategoryName,
    int QuantityTotal,
    int QuantityAvailable,
    int OnLoanQuantity,
    string Condition,
    Guid? RoomId,
    string? RoomName,
    string? LocationLabel,
    decimal? UnitPrice,
    bool IsConsumable,
    uint RowVersion);
