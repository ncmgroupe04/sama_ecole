using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetStockMovements;

/// <summary>
/// GET /api/v1/inventory/movements — le journal de stock de l'école, filtrable et paginé
/// (docs/Volume_4_API_Design.md §0.2). Lecture seule par nature : le journal est append-only, aucun
/// endpoint ne le corrige.
/// </summary>
public record GetStockMovementsQuery : IRequest<PaginatedStockMovements>
{
    public Guid? ItemId { get; init; }

    public StockMovementType? Type { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public record PaginatedStockMovements(
    IReadOnlyList<StockMovementListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record StockMovementListItem(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string? ItemCode,
    string Type,
    int Quantity,
    DateOnly MovementDate,
    string Reason,
    string? CounterpartyLabel,
    Guid? AssignmentId,
    int QuantityTotalAfter,
    int QuantityAvailableAfter);
