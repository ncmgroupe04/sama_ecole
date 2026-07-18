using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Subscriptions.Commands.InitiateSubscriptionPayment;

/// <summary>
/// Ticket JGK-I05 — POST /subscriptions/{schoolId}/payments (Directeur). <paramref name="SchoolId"/>
/// vient du SEGMENT DE ROUTE, jamais utilisé pour résoudre le tenant (AGENTS.md règle #10 — c'est
/// ITenantProvider.CurrentSchoolId, dérivé du JWT, qui gouverne toute écriture) : il ne sert qu'à
/// vérifier que l'appelant ne cible pas, par erreur ou malice, l'école d'un autre établissement dans
/// l'URL — voir InitiateSubscriptionPaymentHandler.
/// </summary>
public record InitiateSubscriptionPaymentCommand : IRequest<InitiateSubscriptionPaymentResult>
{
    public required Guid SchoolId { get; init; }
    public required SubscriptionPaymentMethod Method { get; init; }
    public required BillingPeriod BillingPeriod { get; init; }
}

/// <summary>Miroir exact du schéma SubscriptionPaymentInitiateResult déjà documenté dans openapi.yaml.</summary>
public record InitiateSubscriptionPaymentResult(Guid PaymentId, string RedirectUrl, SubscriptionPaymentStatus Status);
