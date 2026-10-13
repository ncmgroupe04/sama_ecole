using FluentAssertions;
using SamaEcole.Application.Subscriptions.Pricing;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>Calcul du devis par tranche (grille de départ du 2026-10-13) et validation de l'édition Super Admin.</summary>
public class QuotaTierPricingTests
{
    private static TierPrice Price(StudentQuotaTier tier, decimal monthly, decimal annual, decimal surchargeM = 0, decimal surchargeA = 0) =>
        new(tier, monthly, annual, surchargeM, surchargeA);

    [Fact]
    public void A_Priced_Tier_Returns_Its_Monthly_And_Annual_Amounts()
    {
        var quote = QuotaTierPricing.Quote(StudentQuotaTier.Tier_100, Price(StudentQuotaTier.Tier_100, 5_000, 50_000), hasBoarding: false);

        quote.RequiresQuote.Should().BeFalse();
        quote.MonthlyXof.Should().Be(5_000);
        quote.AnnualXof.Should().Be(50_000);
        quote.AmountFor(BillingPeriod.Monthly).Should().Be(5_000);
        quote.AmountFor(BillingPeriod.Yearly).Should().Be(50_000);
    }

    [Fact]
    public void The_Boarding_Surcharge_Is_Added_Only_When_The_School_Has_Boarding()
    {
        var price = Price(StudentQuotaTier.Tier2_400, 25_000, 250_000, surchargeM: 5_000, surchargeA: 50_000);

        var without = QuotaTierPricing.Quote(StudentQuotaTier.Tier2_400, price, hasBoarding: false);
        var with = QuotaTierPricing.Quote(StudentQuotaTier.Tier2_400, price, hasBoarding: true);

        without.MonthlyXof.Should().Be(25_000);
        without.IncludesBoarding.Should().BeFalse();
        with.MonthlyXof.Should().Be(30_000);
        with.AnnualXof.Should().Be(300_000);
        with.IncludesBoarding.Should().BeTrue();
    }

    [Fact]
    public void A_Boarding_School_Pays_The_Base_Price_When_No_Surcharge_Is_Configured()
    {
        var quote = QuotaTierPricing.Quote(StudentQuotaTier.Tier_250, Price(StudentQuotaTier.Tier_250, 15_000, 150_000), hasBoarding: true);

        quote.MonthlyXof.Should().Be(15_000);
        quote.AnnualXof.Should().Be(150_000);
    }

    [Theory]
    [InlineData(StudentQuotaTier.Tier3_800)]
    [InlineData(StudentQuotaTier.Tier4_Custom)]
    public void Tiers_Above_The_Last_Standard_Band_Are_On_Quote_With_No_Amount(StudentQuotaTier tier)
    {
        var quote = QuotaTierPricing.Quote(tier, price: null, hasBoarding: true);

        quote.RequiresQuote.Should().BeTrue();
        quote.MonthlyXof.Should().BeNull();
        quote.AnnualXof.Should().BeNull();
    }

    [Fact]
    public void A_Priced_Tier_Without_A_Price_Row_Fails_Loudly()
    {
        var act = () => QuotaTierPricing.Quote(StudentQuotaTier.Tier1_150, price: null, hasBoarding: false);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void A_Price_Row_Of_Another_Tier_Is_Never_Used()
    {
        var act = () => QuotaTierPricing.Quote(
            StudentQuotaTier.Tier1_150, Price(StudentQuotaTier.Tier_100, 5_000, 50_000), hasBoarding: false);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void The_Priced_Tiers_Are_The_Four_Standard_Bands_In_Increasing_Order()
    {
        QuotaTierPricing.PricedTiers().Should().Equal(
            StudentQuotaTier.Tier_100, StudentQuotaTier.Tier1_150, StudentQuotaTier.Tier_250, StudentQuotaTier.Tier2_400);
    }
}

public class UpdateQuotaTierPriceCommandValidatorTests
{
    private readonly UpdateQuotaTierPriceCommandValidator _validator = new();

    private static UpdateQuotaTierPriceCommand Command(
        StudentQuotaTier tier = StudentQuotaTier.Tier_100, decimal monthly = 5_000, decimal annual = 50_000,
        decimal surchargeM = 0, decimal surchargeA = 0) => new()
    {
        Tier = tier,
        MonthlyXof = monthly,
        AnnualXof = annual,
        BoardingSurchargeMonthlyXof = surchargeM,
        BoardingSurchargeAnnualXof = surchargeA
    };

    [Fact]
    public void The_Starting_Grid_Is_Valid() =>
        _validator.Validate(Command()).IsValid.Should().BeTrue();

    [Fact]
    public void A_Yearly_Price_Equal_To_Twelve_Monthly_Payments_Is_Valid() =>
        _validator.Validate(Command(monthly: 5_000, annual: 60_000)).IsValid.Should().BeTrue();

    [Fact]
    public void A_Yearly_Price_Above_Twelve_Monthly_Payments_Is_Refused() =>
        _validator.Validate(Command(monthly: 5_000, annual: 60_100)).Errors
            .Should().ContainSingle(e => e.PropertyName == nameof(UpdateQuotaTierPriceCommand.AnnualXof));

    [Theory]
    [InlineData(StudentQuotaTier.Tier3_800)]
    [InlineData(StudentQuotaTier.Tier4_Custom)]
    public void A_Tier_On_Quote_Has_No_Price_To_Edit(StudentQuotaTier tier) =>
        _validator.Validate(Command(tier: tier)).Errors
            .Should().Contain(e => e.PropertyName == nameof(UpdateQuotaTierPriceCommand.Tier));

    [Fact]
    public void An_Unknown_Tier_Is_Refused() =>
        _validator.Validate(Command(tier: (StudentQuotaTier)99)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50_000_001)]
    public void The_Base_Price_Must_Be_Positive_And_Plausible(decimal monthly) =>
        _validator.Validate(Command(monthly: monthly, annual: 1)).Errors
            .Should().Contain(e => e.PropertyName == nameof(UpdateQuotaTierPriceCommand.MonthlyXof));

    [Fact]
    public void Amounts_Must_Be_Whole_Francs() =>
        _validator.Validate(Command(monthly: 5_000.5m)).Errors
            .Should().Contain(e => e.PropertyName == nameof(UpdateQuotaTierPriceCommand.MonthlyXof));

    [Fact]
    public void A_Negative_Surcharge_Is_Refused() =>
        _validator.Validate(Command(surchargeM: -1)).Errors
            .Should().Contain(e => e.PropertyName == nameof(UpdateQuotaTierPriceCommand.BoardingSurchargeMonthlyXof));

    [Fact]
    public void A_Yearly_Surcharge_Above_Twelve_Monthly_Ones_Is_Refused() =>
        _validator.Validate(Command(surchargeM: 1_000, surchargeA: 12_001)).Errors
            .Should().ContainSingle(e => e.PropertyName == nameof(UpdateQuotaTierPriceCommand.BoardingSurchargeAnnualXof));
}
