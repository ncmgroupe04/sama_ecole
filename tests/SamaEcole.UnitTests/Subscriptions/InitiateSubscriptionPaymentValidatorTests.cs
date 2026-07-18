using FluentAssertions;
using SamaEcole.Application.Subscriptions.Commands.InitiateSubscriptionPayment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

public class InitiateSubscriptionPaymentValidatorTests
{
    private readonly InitiateSubscriptionPaymentValidator _validator = new();

    [Fact]
    public void Should_Succeed_For_A_Well_Formed_Command()
    {
        var result = _validator.Validate(new InitiateSubscriptionPaymentCommand
        {
            SchoolId = Guid.NewGuid(),
            Method = SubscriptionPaymentMethod.MobileMoney,
            BillingPeriod = BillingPeriod.Monthly
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_SchoolId_Is_Empty()
    {
        var result = _validator.Validate(new InitiateSubscriptionPaymentCommand
        {
            SchoolId = Guid.Empty,
            Method = SubscriptionPaymentMethod.MobileMoney,
            BillingPeriod = BillingPeriod.Monthly
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_Method_Is_Not_A_Valid_Enum_Value()
    {
        var result = _validator.Validate(new InitiateSubscriptionPaymentCommand
        {
            SchoolId = Guid.NewGuid(),
            Method = (SubscriptionPaymentMethod)999,
            BillingPeriod = BillingPeriod.Monthly
        });

        result.IsValid.Should().BeFalse();
    }
}
