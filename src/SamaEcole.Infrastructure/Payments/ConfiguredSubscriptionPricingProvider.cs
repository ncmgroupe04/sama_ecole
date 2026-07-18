using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Payments;

/// <summary>
/// Ticket JGK-I05 — lit le tarif depuis la configuration (voir SubscriptionPricingOptions, valeurs
/// PLACEHOLDER à ajuster). Une future évolution pourrait piloter les tarifs depuis une table gérée par
/// le Super Admin sans que ISubscriptionPricingProvider ni ses appelants n'aient à changer.
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
}
