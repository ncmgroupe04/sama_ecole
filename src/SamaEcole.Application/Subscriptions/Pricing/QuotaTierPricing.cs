using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Pricing;

/// <summary>Prix d'une tranche tarifée, tel que lu en base (voir <c>QuotaTierPrice</c>), en FCFA.</summary>
public sealed record TierPrice(
    StudentQuotaTier Tier,
    decimal MonthlyXof,
    decimal AnnualXof,
    decimal BoardingSurchargeMonthlyXof,
    decimal BoardingSurchargeAnnualXof);

/// <summary>
/// Devis d'une tranche. Au-delà de la dernière tranche standard (<see cref="RequiresQuote"/>), aucun montant :
/// l'offre « Grand Groupe » se négocie, elle ne se calcule pas.
/// </summary>
/// <param name="IncludesBoarding">Vrai si la majoration Internat est comprise dans les montants.</param>
public sealed record PricingQuote(
    StudentQuotaTier Tier,
    bool RequiresQuote,
    bool IncludesBoarding,
    decimal? MonthlyXof,
    decimal? AnnualXof)
{
    public decimal? AmountFor(BillingPeriod period) => period == BillingPeriod.Monthly ? MonthlyXof : AnnualXof;
}

/// <summary>
/// Calcul du devis, SANS accès base de données (testable à plat). Le prix de base vient de la ligne de la
/// tranche ; l'Internat y ajoute sa majoration, 0 tant que le Super Admin n'en a pas fixé.
/// </summary>
public static class QuotaTierPricing
{
    /// <param name="price">Ligne de la tranche ; <c>null</c> accepté uniquement pour une tranche sur devis.</param>
    public static PricingQuote Quote(StudentQuotaTier tier, TierPrice? price, bool hasBoarding)
    {
        if (QuotaTierResolver.RequiresQuote(tier))
        {
            return new PricingQuote(tier, RequiresQuote: true, IncludesBoarding: false, MonthlyXof: null, AnnualXof: null);
        }

        if (price is null || price.Tier != tier)
        {
            throw new KeyNotFoundException($"Aucun tarif n'est configuré pour la tranche {tier}.");
        }

        return new PricingQuote(
            tier,
            RequiresQuote: false,
            IncludesBoarding: hasBoarding,
            MonthlyXof: price.MonthlyXof + (hasBoarding ? price.BoardingSurchargeMonthlyXof : 0m),
            AnnualXof: price.AnnualXof + (hasBoarding ? price.BoardingSurchargeAnnualXof : 0m));
    }

    /// <summary>Tranches pour lesquelles une ligne de prix doit exister, de la plus petite à la plus grande.</summary>
    public static IEnumerable<StudentQuotaTier> PricedTiers() =>
        QuotaTierResolver.Ladder.Where(t => !QuotaTierResolver.RequiresQuote(t));
}
