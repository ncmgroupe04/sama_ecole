using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Inventory.Commands.CreateInventoryItem;

/// <summary>
/// POST /api/v1/inventory/items — crée un LOT de biens. Le SchoolId vient du JWT (AGENTS.md règle #10).
///
/// <see cref="InitialQuantity"/> est la SEULE occasion de fixer une quantité directement, et elle ne
/// contourne pas le journal pour autant : le Handler la traduit en un mouvement d'entrée dans la même
/// transaction. Toute variation ultérieure passe par /inventory/movements ou par une fiche de prêt.
/// </summary>
public record CreateInventoryItemCommand : IRequest<InventoryItemResult>
{
    public required string Name { get; init; }

    /// <summary>Code d'inventaire ou code-barres, facultatif et en saisie libre (voir InventoryItem.Code).</summary>
    public string? Code { get; init; }

    public Guid CategoryId { get; init; }

    public int InitialQuantity { get; init; }

    public ItemCondition Condition { get; init; } = ItemCondition.Bon;

    /// <summary>Salle d'entreposage (module Infrastructures), si l'école a saisi ses bâtiments.</summary>
    public Guid? RoomId { get; init; }

    /// <summary>Emplacement en texte libre, pour un local qui n'est pas une salle (« Réserve A »).</summary>
    public string? LocationLabel { get; init; }

    public decimal? UnitPrice { get; init; }

    public bool IsConsumable { get; init; }

    public string? Notes { get; init; }
}

public record InventoryItemResult(
    Guid Id,
    string Name,
    string? Code,
    Guid CategoryId,
    int QuantityTotal,
    int QuantityAvailable,
    string Condition,
    Guid? RoomId,
    string? LocationLabel,
    decimal? UnitPrice,
    bool IsConsumable,
    uint RowVersion);
