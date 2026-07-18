using FluentAssertions;
using SamaEcole.Application.Reports.Queries.GetAttendanceExport;
using Xunit;

namespace SamaEcole.UnitTests.Reports;

public class GetAttendanceExportQueryValidatorTests
{
    private readonly GetAttendanceExportQueryValidator _validator = new();

    private static GetAttendanceExportQuery Valid() => new()
    {
        StartDate = new DateOnly(2026, 7, 1),
        EndDate = new DateOnly(2026, 7, 31),
        Format = "pdf"
    };

    [Theory]
    [InlineData("pdf")]
    [InlineData("csv")]
    [InlineData("PDF")]
    [InlineData("Csv")]
    public void Accepted_Formats_Should_Pass(string format)
    {
        _validator.Validate(Valid() with { Format = format }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("xml")]
    [InlineData("xlsx")]
    public void Unknown_Format_Should_Fail(string format)
    {
        var result = _validator.Validate(Valid() with { Format = format });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetAttendanceExportQuery.Format));
    }

    [Fact]
    public void End_Before_Start_Should_Fail()
    {
        var query = Valid() with { StartDate = new DateOnly(2026, 7, 31), EndDate = new DateOnly(2026, 7, 1) };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Range_Beyond_The_Cap_Should_Fail()
    {
        var query = Valid() with { StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2026, 12, 31) };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }
}
