using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CancelPayment;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CancelPaymentCommandValidatorTests
{
    private readonly CancelPaymentCommandValidator _validator = new();

    [Fact]
    public void A_Payment_Id_And_A_Reason_Are_Valid()
    {
        _validator.Validate(new CancelPaymentCommand(Guid.NewGuid(), "Saisi sur le mauvais élève"))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public void A_Missing_Or_Too_Short_Reason_Is_Refused(string reason)
    {
        _validator.Validate(new CancelPaymentCommand(Guid.NewGuid(), reason))
            .Errors.Should().Contain(e => e.PropertyName == nameof(CancelPaymentCommand.Reason));
    }

    [Fact]
    public void A_Reason_Longer_Than_500_Characters_Is_Refused()
    {
        _validator.Validate(new CancelPaymentCommand(Guid.NewGuid(), new string('x', 501)))
            .Errors.Should().Contain(e => e.PropertyName == nameof(CancelPaymentCommand.Reason));
    }

    [Fact]
    public void An_Empty_Payment_Id_Is_Refused()
    {
        _validator.Validate(new CancelPaymentCommand(Guid.Empty, "Erreur de saisie"))
            .Errors.Should().Contain(e => e.PropertyName == nameof(CancelPaymentCommand.PaymentId));
    }
}
