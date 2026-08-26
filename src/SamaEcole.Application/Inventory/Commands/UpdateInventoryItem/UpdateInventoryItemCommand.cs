using SamaEcole.Application.Inventory.Commands.CreateInventoryItem;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Inventory.Commands.UpdateInventoryItem;

/// <summary>
/// PUT /api/v1/inventory/items/{id} — corrige la FICHE d'un lot : libellé, code, catégorie,
/// emplacement, état, prix indicatif.
///
/// Aucune quantité ici, et c'est le point central du module : les compteurs d'un lot ne se modifient
/// que par un mouvement de stock journalisé (/inventory/movements) ou par une fiche de prêt. Ouvrir
/// une écriture directe sur QuantityTotal/QuantityAvailable, c'est autoriser un inventaire dont le
/// journal ne rend plus compte — donc inopposable lors d'un contrôle.
/// </summary>
public record UpdateInventoryItemCommand : IRequest<InventoryItemResult>
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Code { get; init; }
    public Guid CategoryId { get; init; }
    public ItemCondition Condition { get; init; }
    public Guid? RoomId { get; init; }
    public string? LocationLabel { get; init; }
    public decimal? UnitPrice { get; init; }
    public bool IsConsumable { get; init; }
    public string? Notes { get; init; }
    public uint RowVersion { get; init; }
}
