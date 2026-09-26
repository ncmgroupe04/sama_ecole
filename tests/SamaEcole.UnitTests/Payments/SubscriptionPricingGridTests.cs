using FluentAssertions;
using Microsoft.Extensions.Options;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Payments;
using Xunit;

namespace SamaEcole.UnitTests.Payments;

/// <summary>
/// La grille tarifaire de la vitrine : montants annuels par cycles et taille (SubscriptionPricing:Grid) et règles
/// qui en déduisent le plan d'abonnement. Les valeurs attendues sont celles PUBLIÉES sur /vitrine.
/// </summary>
public class SubscriptionPricingGridTests
{
    // Options par défaut = la grille publiée, sans configuration : c'est ce qu'un déploiement sans surcharge facture.
    private static readonly ConfiguredSubscriptionPricingProvider Provider = new(Options.Create(new SubscriptionPricingOptions()));

    [Theory]
    [InlineData(SchoolCycleProfile.Primaire, SchoolSizeTier.Small, 150_000)]
    [InlineData(SchoolCycleProfile.Primaire, SchoolSizeTier.Medium, 250_000)]
    [InlineData(SchoolCycleProfile.Primaire, SchoolSizeTier.Large, 350_000)]
    [InlineData(SchoolCycleProfile.College, SchoolSizeTier.Small, 250_000)]
    [InlineData(SchoolCycleProfile.College, SchoolSizeTier.Medium, 400_000)]
    [InlineData(SchoolCycleProfile.College, SchoolSizeTier.Large, 550_000)]
    [InlineData(SchoolCycleProfile.Lycee, SchoolSizeTier.Small, 300_000)]
    [InlineData(SchoolCycleProfile.Lycee, SchoolSizeTier.Medium, 450_000)]
    [InlineData(SchoolCycleProfile.Lycee, SchoolSizeTier.Large, 600_000)]
    [InlineData(SchoolCycleProfile.Bicycle, SchoolSizeTier.Small, 450_000)]
    [InlineData(SchoolCycleProfile.Bicycle, SchoolSizeTier.Medium, 650_000)]
    [InlineData(SchoolCycleProfile.Bicycle, SchoolSizeTier.Large, 850_000)]
    [InlineData(SchoolCycleProfile.Complexe, SchoolSizeTier.Small, 600_000)]
    [InlineData(SchoolCycleProfile.Complexe, SchoolSizeTier.Medium, 850_000)]
    [InlineData(SchoolCycleProfile.Complexe, SchoolSizeTier.Large, 1_200_000)]
    public void Should_Return_The_Published_Annual_Amount_For_Each_Offer(
        SchoolCycleProfile profile, SchoolSizeTier tier, decimal expected)
        => Provider.GetGridAmount(profile, tier).Should().Be(expected);

    [Fact]
    public void A_Configured_Grid_Overrides_The_Defaults()
    {
        var provider = new ConfiguredSubscriptionPricingProvider(Options.Create(new SubscriptionPricingOptions
        {
            Grid = new GridPricing { Bicycle = new TierPricing { Small = 1m, Medium = 2m, Large = 3m } }
        }));

        provider.GetGridAmount(SchoolCycleProfile.Bicycle, SchoolSizeTier.Medium).Should().Be(2m);
    }

    [Theory]
    [InlineData(SchoolCycleProfile.Primaire, SubscriptionPlan.Primaire)]
    [InlineData(SchoolCycleProfile.College, SubscriptionPlan.Primaire)]
    [InlineData(SchoolCycleProfile.Lycee, SubscriptionPlan.Primaire)]
    [InlineData(SchoolCycleProfile.Bicycle, SubscriptionPlan.Standard)]
    [InlineData(SchoolCycleProfile.Complexe, SubscriptionPlan.Standard)]
    public void The_Plan_Is_Derived_From_The_Cycles_Never_From_A_Choice(SchoolCycleProfile profile, SubscriptionPlan expected)
        => SubscriptionPricingGrid.PlanFor(profile).Should().Be(expected);

    [Fact]
    public void No_Grid_Offer_Grants_The_Premium_Plan()
        => Enum.GetValues<SchoolCycleProfile>()
            .Select(SubscriptionPricingGrid.PlanFor)
            .Should().NotContain(SubscriptionPlan.Premium, "les SMS aux parents passent par un devis, pas par la grille");

    [Theory]
    [InlineData(SchoolOwnership.Private, SchoolCycleProfile.Bicycle, SchoolSizeTier.Medium, true)]
    [InlineData(SchoolOwnership.Private, SchoolCycleProfile.Primaire, null, false)]
    [InlineData(SchoolOwnership.Public, SchoolCycleProfile.College, null, true)]
    [InlineData(SchoolOwnership.Public, SchoolCycleProfile.Bicycle, null, false)]
    [InlineData(SchoolOwnership.Public, SchoolCycleProfile.Complexe, null, false)]
    public void Only_Combinations_Of_The_Grid_Are_Valid(
        SchoolOwnership ownership, SchoolCycleProfile profile, SchoolSizeTier? tier, bool expected)
        => SubscriptionPricingGrid.IsValidCombination(ownership, profile, tier).Should().Be(expected);
}
