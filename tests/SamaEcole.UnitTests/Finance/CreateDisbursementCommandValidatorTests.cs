using FluentAssertions;
using SamaEcole.Application.Features.Disbursements;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CreateDisbursementCommandValidatorTests
{
    private readonly CreateDisbursementCommandValidator _validator = new();

    private static CreateDisbursementCommand Valid() => new(
        Reason: "Fournitures de bureau",
        Category: DisbursementCategory.Fournitures,
        Amount: 25_000m,
        PaymentMethod: PaymentMethod.Cash,
        Date: new DateOnly(2026, 3, 10),
        Beneficiary: "Papeterie du Marché",
        ReceiptUrl: null);

    [Fact]
    public void A_Well_Formed_Disbursement_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Null_Vat_Rate_Is_Accepted_As_Not_Subject_To_Vat()
    {
        _validator.Validate(Valid() with { VatRate = null }).IsValid.Should().BeTrue("ex. Salaires n'est jamais assujettie");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.18)]
    [InlineData(1)]
    public void A_Vat_Rate_Within_Zero_And_One_Is_Accepted(decimal vatRate)
    {
        _validator.Validate(Valid() with { VatRate = vatRate }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(18)]
    public void A_Vat_Rate_Outside_Zero_And_One_Is_Rejected(decimal vatRate)
    {
        var result = _validator.Validate(Valid() with { VatRate = vatRate });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateDisbursementCommand.VatRate));
    }

    [Fact]
    public void An_Empty_Reason_Is_Rejected()
    {
        _validator.Validate(Valid() with { Reason = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Non_Positive_Amount_Is_Rejected()
    {
        _validator.Validate(Valid() with { Amount = 0m }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Beneficiary_Is_Rejected()
    {
        _validator.Validate(Valid() with { Beneficiary = "" }).IsValid.Should().BeFalse();
    }
}
