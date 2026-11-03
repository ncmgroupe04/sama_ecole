using FluentAssertions;
using SamaEcole.Application.Platform.Commands.GrantComplimentaryAccess;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

public class GrantComplimentaryAccessCommandTests
{
    private readonly GrantComplimentaryAccessCommandValidator _validator = new();

    private static GrantComplimentaryAccessCommand Command(int? months = null, int? days = null) => new()
    {
        SchoolId = Guid.NewGuid(),
        Plan = SubscriptionPlan.Standard,
        DurationMonths = months,
        DurationDays = days
    };

    [Theory]
    [InlineData(1, null)]
    [InlineData(60, null)]
    [InlineData(null, 1)]
    [InlineData(null, 10)]
    [InlineData(null, 1825)]
    public void A_Duration_In_Months_Or_In_Days_Should_Be_Accepted(int? months, int? days)
    {
        _validator.Validate(Command(months, days)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(61, null)]
    [InlineData(null, 0)]
    [InlineData(null, -3)]
    [InlineData(null, 1826)]
    public void A_Duration_Out_Of_Range_Should_Be_Rejected(int? months, int? days)
    {
        _validator.Validate(Command(months, days)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Giving_Both_Months_And_Days_Should_Be_Rejected()
    {
        _validator.Validate(Command(months: 1, days: 10)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Giving_Neither_Months_Nor_Days_Should_Be_Rejected()
    {
        _validator.Validate(Command()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Duration_In_Days_Should_End_That_Many_Days_After_Today()
    {
        Command(days: 10).ExpiresAtFrom(new DateOnly(2026, 11, 2)).Should().Be(new DateOnly(2026, 11, 12));
    }

    [Fact]
    public void A_Duration_In_Days_Should_Cross_A_Month_And_A_Year_Boundary()
    {
        Command(days: 30).ExpiresAtFrom(new DateOnly(2026, 12, 15)).Should().Be(new DateOnly(2027, 1, 14));
    }

    [Fact]
    public void A_Duration_In_Months_Should_Keep_Its_Previous_Meaning()
    {
        Command(months: 1).ExpiresAtFrom(new DateOnly(2026, 11, 2)).Should().Be(new DateOnly(2026, 12, 2));
        Command(months: 3).ExpiresAtFrom(new DateOnly(2026, 11, 2)).Should().Be(new DateOnly(2027, 2, 2));
    }

    [Fact]
    public void A_Duration_In_Days_Replaces_The_Current_Expiry_It_Does_Not_Extend_It()
    {
        // Un client qui a accès jusqu'au 11/01/2027 et reçoit 10 jours le 02/11/2026 : fin au 12/11/2026, pas au 21/01/2027.
        var expiresAt = Command(days: 10).ExpiresAtFrom(new DateOnly(2026, 11, 2));

        expiresAt.Should().BeBefore(new DateOnly(2027, 1, 11));
    }
}
