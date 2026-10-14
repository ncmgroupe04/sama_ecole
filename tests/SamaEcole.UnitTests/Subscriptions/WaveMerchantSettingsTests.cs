using FluentAssertions;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Application.Subscriptions.Commands.InitiateSubscriptionPayment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

public class WaveMerchantSettingsTests
{
    [Theory]
    [InlineData("https://pay.wave.com/m/M_RllSqNXcTsCF/c/sn/", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("pay.wave.com/m/M_xxx", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("http://pay.wave.com/m/M_xxx/c/sn/", false)]
    public void IsConfigured_Only_Accepts_An_Absolute_Https_Url(string url, bool expected)
    {
        new WaveMerchantSettings { PaymentUrl = url }.IsConfigured.Should().Be(expected);
    }

    [Fact]
    public void The_Validator_Accepts_The_WaveMerchant_Method()
    {
        var result = new InitiateSubscriptionPaymentValidator().Validate(new InitiateSubscriptionPaymentCommand
        {
            SchoolId = Guid.NewGuid(),
            Method = SubscriptionPaymentMethod.WaveMerchant,
            BillingPeriod = BillingPeriod.Yearly
        });

        result.IsValid.Should().BeTrue();
    }
}
