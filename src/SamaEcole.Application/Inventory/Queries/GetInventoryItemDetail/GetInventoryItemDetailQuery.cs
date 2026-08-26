using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryItemDetail;

/// <summary>
/// GET /api/v1/inventory/items/{id} — la fiche d'un lot, son historique récent et ses prêts ouverts,
/// en UNE réponse : c'est exactement ce qu'affiche l'écran de détail, et trois allers-retours pour
/// une seule page n'apporteraient rien.
/// </summary>
public record GetInventoryItemDetailQuery(Guid Id) : IRequest<InventoryItemDetailDto>;

public record InventoryItemDetailDto(
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
    string? Notes,
    uint RowVersion,
    IReadOnlyList<InventoryItemMovementDto> RecentMovements,
    IReadOnlyList<InventoryItemOpenAssignmentDto> OpenAssignments);

public record InventoryItemMovementDto(
    Guid Id,
    string Type,
    int Quantity,
    DateOnly MovementDate,
    string Reason,
    string? CounterpartyLabel,
    int QuantityTotalAfter,
    int QuantityAvailableAfter);

public record InventoryItemOpenAssignmentDto(
    Guid Id,
    string Reference,
    string BeneficiaryType,
    string BeneficiaryLabel,
    int Quantity,
    int ReturnedQuantity,
    DateOnly AssignedOn,
    DateOnly? DueOn,
    bool IsOverdue,
    string Status,
    uint RowVersion);
