using FluentAssertions;
using SamaEcole.Application.Platform.Commands.CreatePromoCode;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

public class CreatePromoCodeCommandValidatorTests
{
    private readonly CreatePromoCodeCommandValidator _validator = new();

    private static CreatePromoCodeCommand Command(
        PromoDiscountType discountType,
        decimal discountValue = 10m,
        int? durationMonths = null,
        int? maxUses = null,
        string code = "BIENVENUE2026") => new()
    {
        Code = code,
        DiscountType = discountType,
        DiscountValue = discountValue,
        DurationMonths = durationMonths,
        MaxUses = maxUses,
        StartDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EndDateUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void A_Valid_Percentage_Code_Should_Be_Accepted()
    {
        _validator.Validate(Command(PromoDiscountType.Percentage, discountValue: 20m)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public void A_Percentage_Outside_0_To_100_Should_Be_Rejected(decimal value)
    {
        _validator.Validate(Command(PromoDiscountType.Percentage, discountValue: value)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_FreeTrialMonths_Code_Without_A_Duration_Should_Be_Rejected()
    {
        // Sans DurationMonths, rien ne pilote la durée offerte (règle #11 : DiscountValue est ignoré
        // pour ce type — DurationMonths est la SEULE donnée qui compte).
        var result = _validator.Validate(Command(PromoDiscountType.FreeTrialMonths, durationMonths: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePromoCodeCommand.DurationMonths));
    }

    [Fact]
    public void A_FreeTrialMonths_Code_With_A_Duration_Should_Be_Accepted()
    {
        _validator.Validate(Command(PromoDiscountType.FreeTrialMonths, durationMonths: 3)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_FullDiscount_Code_Without_A_Duration_Should_Be_Rejected()
    {
        _validator.Validate(Command(PromoDiscountType.FullDiscount, durationMonths: null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_End_Date_Before_The_Start_Date_Should_Be_Rejected()
    {
        var command = Command(PromoDiscountType.Percentage) with
        {
            StartDateUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("code avec espaces")]
    [InlineData("<script>alert(1)</script>")]
    public void An_Invalid_Code_Format_Should_Be_Rejected(string code)
    {
        _validator.Validate(Command(PromoDiscountType.Percentage, code: code)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Zero_Max_Uses_Should_Be_Rejected()
    {
        _validator.Validate(Command(PromoDiscountType.Percentage, maxUses: 0)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Null_Max_Uses_Should_Be_Accepted_As_Unlimited()
    {
        _validator.Validate(Command(PromoDiscountType.Percentage, maxUses: null)).IsValid.Should().BeTrue();
    }
}
