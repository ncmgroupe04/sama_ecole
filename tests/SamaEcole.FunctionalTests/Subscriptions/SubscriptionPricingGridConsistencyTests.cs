using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Web.Content;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Garde-fou : les montants PUBLIÉS sur la vitrine (MarketingCatalog.Pricing, texte) et ceux que l'application FACTURE
/// (SubscriptionPricing:Grid de appsettings.json, lus via ISubscriptionPricingProvider) ne doivent jamais diverger.
/// Changer un prix d'un côté sans l'autre fait échouer ce test — un client ne doit pas payer autre chose que ce qu'on
/// lui a affiché.
/// </summary>
public class SubscriptionPricingGridConsistencyTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly Dictionary<string, SchoolCycleProfile> ProfileByOfferName = new()
    {
        ["Primaire"] = SchoolCycleProfile.Primaire,
        ["Collège"] = SchoolCycleProfile.College,
        ["Lycée"] = SchoolCycleProfile.Lycee,
        ["Bicycle"] = SchoolCycleProfile.Bicycle,
        ["Grand complexe"] = SchoolCycleProfile.Complexe
    };

    /// <summary>« 150 000 FCFA » (espaces insécables) → 150000 ; « 1,2 million FCFA » → 1 200 000.</summary>
    private static decimal ParsePrice(string text)
    {
        if (text.Contains("million", StringComparison.OrdinalIgnoreCase))
        {
            var millions = decimal.Parse(Regex.Match(text, @"[\d,]+").Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            return millions * 1_000_000m;
        }

        return decimal.Parse(Regex.Replace(text, @"\D", string.Empty));
    }

    [Fact]
    public void Every_Private_Offer_Displayed_On_The_Showcase_Matches_The_Billed_Amount()
    {
        var provider = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ISubscriptionPricingProvider>();
        var privateAudience = MarketingCatalog.Pricing.Single(a => a.Key == "prive");
        var checkedOffers = 0;

        foreach (var offer in privateAudience.Groups.SelectMany(g => g.Offers))
        {
            var profile = ProfileByOfferName[offer.Name];
            offer.Tiers.Should().HaveCount(3, offer.Name);

            var tiers = new[] { SchoolSizeTier.Small, SchoolSizeTier.Medium, SchoolSizeTier.Large };
            for (var i = 0; i < tiers.Length; i++)
            {
                provider.GetGridAmount(profile, tiers[i], BillingPeriod.Yearly).Should().Be(ParsePrice(offer.Tiers[i].Price),
                    $"{offer.Name} / {offer.Tiers[i].Label} : le montant facturé doit être celui affiché");
                checkedOffers++;
            }
        }

        checkedOffers.Should().Be(15, "5 offres privées × 3 paliers");
    }

    [Fact]
    public void The_Public_Offers_Are_Per_Student_And_Have_No_Fixed_Billed_Amount()
    {
        var publicAudience = MarketingCatalog.Pricing.Single(a => a.Key == "public");

        publicAudience.Groups.SelectMany(g => g.Offers)
            .Should().OnlyContain(o => o.Unit.Contains("par élève") && o.Tiers.Count == 1);
    }
}
