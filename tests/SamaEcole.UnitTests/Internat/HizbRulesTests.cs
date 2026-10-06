using FluentAssertions;
using SamaEcole.Application.Internat;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Internat;

public class HizbRulesTests
{
    [Theory]
    [InlineData(0, HizbMemorizationState.NotStarted)]
    [InlineData(1, HizbMemorizationState.InProgress)]
    [InlineData(2, HizbMemorizationState.InProgress)]
    [InlineData(3, HizbMemorizationState.InProgress)]
    [InlineData(4, HizbMemorizationState.Completed)]
    public void State_Is_Derived_From_The_Completed_Quarters(int quarters, HizbMemorizationState expected)
    {
        HizbRules.StateFor(quarters).Should().Be(expected);
    }

    [Fact]
    public void The_Quran_Has_240_Quarters()
    {
        HizbRules.TotalQuarters.Should().Be(240);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(6, 2.5)]
    [InlineData(120, 50.0)]
    [InlineData(240, 100.0)]
    public void Progress_Percent_Is_Quarters_Over_240(int quarters, double expected)
    {
        HizbRules.ProgressPercent(quarters).Should().Be((decimal)expected);
    }
}
