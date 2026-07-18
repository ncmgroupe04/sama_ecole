using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace SamaEcole.UnitTests.Payments;

public class ConfiguredSubscriptionPricingProviderTests
{
    private static readonly SubscriptionPricingOptions PricingOptions = new()
    {
        Primaire = new PlanPricing { Monthly = 10_000m, Yearly = 100_000m },
        Standard = new PlanPricing { Monthly = 20_000m, Yearly = 200_000m },
        Premium = new PlanPricing { Monthly = 30_000m, Yearly = 300_000m }
    };

    private static readonly ConfiguredSubscriptionPricingProvider Provider = new(Options.Create(PricingOptions));

    [Theory]
    [InlineData(SubscriptionPlan.Primaire, BillingPeriod.Monthly, 10_000)]
    [InlineData(SubscriptionPlan.Primaire, BillingPeriod.Yearly, 100_000)]
    [InlineData(SubscriptionPlan.Standard, BillingPeriod.Monthly, 20_000)]
    [InlineData(SubscriptionPlan.Standard, BillingPeriod.Yearly, 200_000)]
    [InlineData(SubscriptionPlan.Premium, BillingPeriod.Monthly, 30_000)]
    [InlineData(SubscriptionPlan.Premium, BillingPeriod.Yearly, 300_000)]
    public void Should_Return_The_Configured_Amount_For_Each_Plan_And_Period(
        SubscriptionPlan plan, BillingPeriod period, decimal expected)
    {
        Provider.GetAmount(plan, period).Should().Be(expected);
    }
}
