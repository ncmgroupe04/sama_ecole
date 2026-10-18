using FluentAssertions;
using SamaEcole.Application.Finance;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class PaymentCancellationPolicyTests
{
    [Fact]
    public void The_Grace_Period_Is_48_Hours()
    {
        PaymentCancellationPolicy.GracePeriod.Should().Be(TimeSpan.FromHours(48));
    }

    [Fact]
    public void The_Deadline_Is_48_Hours_After_The_Payment()
    {
        var paidAt = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero); // lundi 09:30

        PaymentCancellationPolicy.DeadlineFor(paidAt)
            .Should().Be(new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero)); // mercredi 09:30
    }
}
