using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Subscriptions.Queries.GetSubscriptionPayments;

/// <summary>
/// Ticket JGK-I07 — GET /subscriptions/{schoolId}/payments (Directeur). <paramref name="SchoolId"/> vient
/// du SEGMENT DE ROUTE, jamais utilisé pour résoudre le tenant (AGENTS.md règle #10, même idiome que
/// InitiateSubscriptionPaymentCommand) : il ne sert qu'à vérifier que l'appelant ne cible pas, par erreur
/// ou malice, l'école d'un autre établissement dans l'URL — voir GetSubscriptionPaymentsQueryHandler.
/// </summary>
public record GetSubscriptionPaymentsQuery : IRequest<PaginatedSubscriptionPayments>
{
    public required Guid SchoolId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public record SubscriptionPaymentListItem(
    Guid Id,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ConfirmedAt,
    decimal Amount,
    string Currency,
    BillingPeriod BillingPeriod,
    SubscriptionPaymentMethod Method,
    SubscriptionPaymentStatus Status,
    string? ProviderTransactionRef);

/// <summary>
/// Le statut/plan/échéance de l'abonnement voyagent ici plutôt que dans un second endpoint : l'écran
/// "Facturation & Historique" (JGK-I07) n'a besoin que d'UN appel pour afficher à la fois le statut actuel
/// et l'historique, et le ticket ne demande explicitement qu'« un endpoint de lecture ».
/// </summary>
public record PaginatedSubscriptionPayments(
    IReadOnlyList<SubscriptionPaymentListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    SubscriptionStatus SubscriptionStatus,
    SubscriptionPlan SubscriptionPlan,
    DateOnly? SubscriptionExpiresAt);
