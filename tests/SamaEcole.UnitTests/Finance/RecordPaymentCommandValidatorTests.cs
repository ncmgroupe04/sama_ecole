using FluentAssertions;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Ticket JGK-F02 — validation de FORME du versement (le contrôle de solde vit dans le Handler, il
/// dépend de l'état en base). On verrouille : montant strictement positif, inscription renseignée,
/// moyen de paiement valide.
/// </summary>
public class RecordPaymentCommandValidatorTests
{
    private readonly RecordPaymentCommandValidator _validator = new();

    private static RecordPaymentCommand Valid() =>
        new(Guid.NewGuid(), 15_000m, PaymentMethod.Cash);

    [Fact]
    public void A_Well_Formed_Payment_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-15000)]
    public void A_Non_Positive_Amount_Is_Rejected(int amount)
    {
        var result = _validator.Validate(Valid() with { Amount = amount });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.Amount));
    }

    [Fact]
    public void An_Empty_Enrollment_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { EnrollmentId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.EnrollmentId));
    }

    [Fact]
    public void An_Unknown_Payment_Method_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Method = (PaymentMethod)999 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.Method));
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Cheque)]
    [InlineData(PaymentMethod.Transfer)]
    [InlineData(PaymentMethod.MobileMoney)]
    public void Every_Supported_Method_Is_Accepted(PaymentMethod method)
    {
        _validator.Validate(Valid() with { Method = method }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Null_Vat_Rate_Is_Accepted_As_Not_Subject_To_Vat()
    {
        _validator.Validate(Valid() with { VatRate = null }).IsValid.Should().BeTrue();
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
    [InlineData(18)] // Piège plausible : saisir "18" en pensant "18 %" au lieu de la fraction 0.18.
    public void A_Vat_Rate_Outside_Zero_And_One_Is_Rejected(decimal vatRate)
    {
        var result = _validator.Validate(Valid() with { VatRate = vatRate });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.VatRate));
    }
}
