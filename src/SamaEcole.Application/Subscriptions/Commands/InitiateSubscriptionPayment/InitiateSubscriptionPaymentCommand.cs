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

    /// <summary>
    /// Optionnel — module Tarification &amp; Promotions. RE-VALIDÉ intégralement côté serveur (jamais
    /// l'aperçu de POST /subscriptions/validate-promo) : voir PromoCodeDiscountCalculator.
    /// </summary>
    public string? PromoCode { get; init; }
}

/// <summary>
/// Miroir du schéma SubscriptionPaymentInitiateResult (openapi.yaml), étendu pour les codes promo
/// sans échange d'argent (FreeTrialMonths/FullDiscount, AGENTS.md règle #11) : dans ce cas
/// <see cref="ActivatedWithoutPayment"/> vaut true, <see cref="RedirectUrl"/> est null — aucun
/// guichet PayDunya à ouvrir, l'abonnement est déjà Actif.
/// </summary>
public record InitiateSubscriptionPaymentResult(
    Guid? PaymentId, string? RedirectUrl, SubscriptionPaymentStatus? Status, bool ActivatedWithoutPayment = false);
