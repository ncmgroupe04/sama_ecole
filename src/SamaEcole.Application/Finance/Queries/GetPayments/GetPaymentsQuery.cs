using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetPayments;

/// <summary>
/// GET /api/v1/finance/payments?page=&amp;pageSize=&amp;search= — Historique paginé des encaissements de l'établissement.
/// </summary>
public record GetPaymentsQuery : IRequest<PaginatedPayments>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
    public string? Method { get; init; }
    public Guid? ClassroomId { get; init; }
}

public record PaymentListItem(
    Guid Id,
    Guid PaymentId,
    string ReceiptNumber,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    decimal Amount,
    string Method,
    string Status,
    DateTimeOffset PaidAt);

public record PaginatedPayments(IReadOnlyList<PaymentListItem> Items, int TotalCount, int Page, int PageSize);
