using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetItemAssignments;

/// <summary>
/// GET /api/v1/inventory/assignments — les fiches de prêt, filtrables et paginées. C'est l'écran
/// « qui détient quoi » : d'où le filtre <see cref="OverdueOnly"/>, qui répond à la seule question
/// vraiment urgente en fin d'année — quels manuels ne sont pas revenus.
/// </summary>
public record GetItemAssignmentsQuery : IRequest<PaginatedItemAssignments>
{
    public Guid? ItemId { get; init; }

    public AssignmentStatus? Status { get; init; }

    public Guid? StudentId { get; init; }

    public Guid? TeacherId { get; init; }

    /// <summary>Prêts encore ouverts dont la date de retour prévue est dépassée.</summary>
    public bool OverdueOnly { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public record PaginatedItemAssignments(
    IReadOnlyList<ItemAssignmentListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record ItemAssignmentListItem(
    Guid Id,
    string Reference,
    Guid ItemId,
    string ItemName,
    string? ItemCode,
    int Quantity,
    int ReturnedQuantity,
    string BeneficiaryType,
    Guid BeneficiaryId,
    string BeneficiaryLabel,
    DateOnly AssignedOn,
    DateOnly? DueOn,
    DateOnly? ReturnedOn,
    string? ReturnCondition,
    bool IsOverdue,
    string Status,
    uint RowVersion);
