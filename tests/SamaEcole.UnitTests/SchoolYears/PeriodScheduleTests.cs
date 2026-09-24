using FluentAssertions;
using SamaEcole.Application.SchoolYears;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.SchoolYears;

public class PeriodScheduleTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2027, 6, 30);

    [Theory]
    [InlineData(EvaluationPeriodType.Trimester, 9, 3)]   // customCount ignoré hors Personnalisé
    [InlineData(EvaluationPeriodType.Semester, 9, 2)]
    [InlineData(EvaluationPeriodType.Custom, 4, 4)]
    public void CountFor_Follows_The_Type(EvaluationPeriodType type, int custom, int expected)
        => PeriodSchedule.CountFor(type, custom).Should().Be(expected);

    [Fact]
    public void Trimester_Labels_Are_The_Historical_Ones()
    {
        PeriodSchedule.Split(Start, End, EvaluationPeriodType.Trimester, 3)
            .Select(p => p.Label)
            .Should().Equal("1er trimestre", "2e trimestre", "3e trimestre");
    }

    [Fact]
    public void Semester_Labels()
    {
        PeriodSchedule.Split(Start, End, EvaluationPeriodType.Semester, 3)
            .Select(p => p.Label)
            .Should().Equal("1er semestre", "2e semestre");
    }

    [Fact]
    public void Custom_Labels_Use_The_Feminine_Ordinal()
    {
        PeriodSchedule.Split(Start, End, EvaluationPeriodType.Custom, 4)
            .Select(p => p.Label)
            .Should().Equal("1re période", "2e période", "3e période", "4e période");
    }

    [Theory]
    [InlineData(EvaluationPeriodType.Trimester, 3)]
    [InlineData(EvaluationPeriodType.Semester, 2)]
    [InlineData(EvaluationPeriodType.Custom, 6)]
    public void Periods_Are_Consecutive_Without_Gap_Or_Overlap_And_Cover_The_Whole_Year(
        EvaluationPeriodType type, int custom)
    {
        var periods = PeriodSchedule.Split(Start, End, type, custom);

        periods[0].Start.Should().Be(Start);
        periods[^1].End.Should().Be(End);
        for (var i = 1; i < periods.Count; i++)
        {
            periods[i].Start.Should().Be(periods[i - 1].End.AddDays(1));
        }
    }

    [Fact]
    public void Trimester_Dates_Are_Identical_To_The_Historical_TermSchedule()
    {
        // Non-régression : les années déjà créées avec l'ancien découpage doivent retomber sur les
        // mêmes bornes quand leurs dates sont modifiées (UpdateSchoolYear).
        var legacy = TermSchedule.Split(Start, End);
        var current = PeriodSchedule.Split(Start, End, EvaluationPeriodType.Trimester, 3);

        current.Select(p => (p.Start, p.End)).Should().Equal(legacy.Select(p => (p.Start, p.End)));
    }

    [Fact]
    public void The_Last_Period_Absorbs_The_Remainder()
    {
        // 10 jours en 3 périodes : 3 + 3 + 4.
        var periods = PeriodSchedule.SplitDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10), 3);

        periods.Select(p => p.End.DayNumber - p.Start.DayNumber + 1).Should().Equal(3, 3, 4);
    }
}
