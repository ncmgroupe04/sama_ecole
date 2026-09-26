using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Payments;

/// <summary>
/// Ticket JGK-I05 — lit le tarif depuis la configuration (voir SubscriptionPricingOptions : la grille de la
/// vitrine pour les établissements issus d'une demande d'inscription, et la tarification par plan, PLACEHOLDER, pour
/// les autres). Une future évolution pourrait piloter les tarifs depuis une table gérée par le Super Admin sans que
/// ISubscriptionPricingProvider ni ses appelants n'aient à changer.
/// </summary>
public class ConfiguredSubscriptionPricingProvider(IOptions<SubscriptionPricingOptions> options)
    : ISubscriptionPricingProvider
{
    public decimal GetAmount(SubscriptionPlan plan, BillingPeriod billingPeriod)
    {
        var pricing = options.Value;

        var planPricing = plan switch
        {
            SubscriptionPlan.Primaire => pricing.Primaire,
            SubscriptionPlan.Standard => pricing.Standard,
            SubscriptionPlan.Premium => pricing.Premium,
            _ => throw new KeyNotFoundException($"Aucun tarif configuré pour le plan {plan}.")
        };

        return billingPeriod == BillingPeriod.Monthly ? planPricing.Monthly : planPricing.Yearly;
    }

    public decimal GetGridAmount(SchoolCycleProfile profile, SchoolSizeTier tier)
    {
        var grid = options.Value.Grid;

        var tiers = profile switch
        {
            SchoolCycleProfile.Primaire => grid.Primaire,
            SchoolCycleProfile.College => grid.College,
            SchoolCycleProfile.Lycee => grid.Lycee,
            SchoolCycleProfile.Bicycle => grid.Bicycle,
            SchoolCycleProfile.Complexe => grid.Complexe,
            _ => throw new KeyNotFoundException($"Aucun tarif de grille pour le profil {profile}.")
        };

        return tier switch
        {
            SchoolSizeTier.Small => tiers.Small,
            SchoolSizeTier.Medium => tiers.Medium,
            SchoolSizeTier.Large => tiers.Large,
            _ => throw new KeyNotFoundException($"Aucun palier de taille {tier} dans la grille.")
        };
    }
}
