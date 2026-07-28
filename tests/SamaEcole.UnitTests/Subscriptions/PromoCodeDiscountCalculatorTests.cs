using FluentAssertions;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>
/// Module Tarification, Réductions &amp; Offres Promotionnelles — seule source de vérité pour
/// l'éligibilité et le calcul d'un code promo (partagée par l'aperçu et la charge réelle).
/// </summary>
public class PromoCodeDiscountCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static PromoCode Build(
        PromoDiscountType type,
        decimal discountValue = 0,
        int? durationMonths = null,
        int? maxUses = null,
        int currentUses = 0,
        bool isActive = true,
        DateTime? startDateUtc = null,
        DateTime? endDateUtc = null) => new()
    {
        Code = "TEST",
        DiscountType = type,
        DiscountValue = discountValue,
        DurationMonths = durationMonths,
        MaxUses = maxUses,
        CurrentUses = currentUses,
        IsActive = isActive,
        StartDateUtc = startDateUtc ?? Now.AddDays(-1).UtcDateTime,
        EndDateUtc = endDateUtc ?? Now.AddDays(30).UtcDateTime
    };

    [Fact]
    public void A_Percentage_Discount_Should_Reduce_The_Base_Amount_Proportionally()
    {
        var promoCode = Build(PromoDiscountType.Percentage, discountValue: 20m);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeTrue();
        result.DiscountedAmount.Should().Be(8_000m);
        result.RequiresPayment.Should().BeTrue("un pourcentage laisse toujours un montant à charger");
    }

    [Fact]
    public void A_Fixed_Amount_Discount_Should_Subtract_A_Flat_Sum()
    {
        var promoCode = Build(PromoDiscountType.FixedAmount, discountValue: 3_000m);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.DiscountedAmount.Should().Be(7_000m);
        result.RequiresPayment.Should().BeTrue();
    }

    [Fact]
    public void A_Fixed_Amount_Discount_Larger_Than_The_Base_Should_Never_Go_Negative()
    {
        var promoCode = Build(PromoDiscountType.FixedAmount, discountValue: 50_000m);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.DiscountedAmount.Should().Be(0m);
    }

    [Fact]
    public void A_FreeTrialMonths_Code_Should_Require_No_Payment()
    {
        var promoCode = Build(PromoDiscountType.FreeTrialMonths, durationMonths: 3);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeTrue();
        result.RequiresPayment.Should().BeFalse(
            "aucun argent ne doit changer de main — pas de SubscriptionPayment ni d'appel à l'agrégateur (règle #11)");
        result.DiscountedAmount.Should().Be(0m);
    }

    [Fact]
    public void A_FullDiscount_Code_Should_Require_No_Payment()
    {
        var promoCode = Build(PromoDiscountType.FullDiscount, durationMonths: 1);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeTrue();
        result.RequiresPayment.Should().BeFalse();
    }

    [Fact]
    public void An_Inactive_Code_Should_Be_Rejected()
    {
        var promoCode = Build(PromoDiscountType.Percentage, discountValue: 10m, isActive: false);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Code_Not_Yet_Started_Should_Be_Rejected()
    {
        var promoCode = Build(PromoDiscountType.Percentage, discountValue: 10m, startDateUtc: Now.AddDays(1).UtcDateTime);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Expired_Code_Should_Be_Rejected()
    {
        var promoCode = Build(PromoDiscountType.Percentage, discountValue: 10m, endDateUtc: Now.AddDays(-1).UtcDateTime);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Code_That_Reached_Its_Usage_Limit_Should_Be_Rejected()
    {
        var promoCode = Build(PromoDiscountType.Percentage, discountValue: 10m, maxUses: 5, currentUses: 5);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Code_Below_Its_Usage_Limit_Should_Still_Be_Accepted()
    {
        var promoCode = Build(PromoDiscountType.Percentage, discountValue: 10m, maxUses: 5, currentUses: 4);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Code_Without_A_Usage_Limit_Should_Always_Be_Accepted()
    {
        var promoCode = Build(PromoDiscountType.Percentage, discountValue: 10m, maxUses: null, currentUses: 1_000_000);

        var result = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount: 10_000m, Now);

        result.IsValid.Should().BeTrue();
    }
}
