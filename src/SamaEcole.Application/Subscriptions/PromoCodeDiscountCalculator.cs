using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// SEULE source de vérité pour l'éligibilité et le calcul d'un code promo — utilisée à la fois par
/// l'aperçu (ValidatePromoCodeQuery) et par la charge réelle (InitiateSubscriptionPaymentHandler),
/// pour que les deux ne puissent jamais diverger. Pure : aucune écriture, aucun incrément de
/// PromoCode.CurrentUses (à la charge de l'appelant, sous verrou xmin).
/// </summary>
public static class PromoCodeDiscountCalculator
{
    public static PromoCodeEvaluation Evaluate(PromoCode promoCode, decimal baseAmount, DateTimeOffset now)
    {
        if (!promoCode.IsActive)
        {
            return PromoCodeEvaluation.Invalid("Ce code promo n'est plus actif.");
        }

        if (now < promoCode.StartDateUtc || now > promoCode.EndDateUtc)
        {
            return PromoCodeEvaluation.Invalid("Ce code promo n'est plus valide.");
        }

        if (promoCode.MaxUses is { } maxUses && promoCode.CurrentUses >= maxUses)
        {
            return PromoCodeEvaluation.Invalid("Ce code promo a atteint sa limite d'utilisations.");
        }

        return promoCode.DiscountType switch
        {
            // RequiresPayment = true : l'argent change toujours de main, l'agrégateur reste sur le
            // chemin — seul le montant transmis change.
            PromoDiscountType.Percentage => PromoCodeEvaluation.Valid(
                Math.Max(0, Math.Round(baseAmount * (1 - promoCode.DiscountValue / 100m), 2)), requiresPayment: true),

            PromoDiscountType.FixedAmount => PromoCodeEvaluation.Valid(
                Math.Max(0, baseAmount - promoCode.DiscountValue), requiresPayment: true),

            // Aucun argent : ni PayDunya ni SubscriptionPayment (AGENTS.md règle #11) — l'appelant
            // active directement l'abonnement pour DurationMonths.
            PromoDiscountType.FreeTrialMonths or PromoDiscountType.FullDiscount =>
                PromoCodeEvaluation.Valid(0m, requiresPayment: false),

            _ => throw new ArgumentOutOfRangeException(nameof(promoCode), promoCode.DiscountType, "Type de réduction inconnu.")
        };
    }
}

public record PromoCodeEvaluation
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public decimal DiscountedAmount { get; init; }

    /// <summary>False pour FreeTrialMonths/FullDiscount : aucun appel à l'agrégateur, aucun SubscriptionPayment.</summary>
    public bool RequiresPayment { get; init; }

    public static PromoCodeEvaluation Invalid(string message) => new() { IsValid = false, ErrorMessage = message };

    public static PromoCodeEvaluation Valid(decimal discountedAmount, bool requiresPayment) =>
        new() { IsValid = true, DiscountedAmount = discountedAmount, RequiresPayment = requiresPayment };
}
