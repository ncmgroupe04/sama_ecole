using FluentAssertions;
using SamaEcole.Application.Reports.Queries.GetAttendanceReport;
using Xunit;

namespace SamaEcole.UnitTests.Reports;

public class GetAttendanceReportQueryValidatorTests
{
    private readonly GetAttendanceReportQueryValidator _validator = new();

    private static GetAttendanceReportQuery Valid() => new()
    {
        StartDate = new DateOnly(2026, 7, 1),
        EndDate = new DateOnly(2026, 7, 31)
    };

    [Fact]
    public void Valid_Query_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Same_Start_And_End_Date_Should_Pass()
    {
        var query = Valid() with { StartDate = new DateOnly(2026, 7, 10), EndDate = new DateOnly(2026, 7, 10) };

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void End_Before_Start_Should_Fail()
    {
        var query = Valid() with { StartDate = new DateOnly(2026, 7, 31), EndDate = new DateOnly(2026, 7, 1) };

        var result = _validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetAttendanceReportQuery.EndDate));
    }

    [Fact]
    public void Range_Beyond_The_Cap_Should_Fail()
    {
        var query = Valid() with
        {
            StartDate = new DateOnly(2025, 1, 1),
            EndDate = new DateOnly(2026, 12, 31) // ~730 jours, au-delà de MaxRangeDays
        };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        var query = Valid() with { PageSize = GetAttendanceReportQueryValidator.MaxPageSize + 1 };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(Valid() with { Page = page }).IsValid.Should().BeFalse();
    }
}
