using FluentAssertions;
using SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;
using Xunit;

namespace SamaEcole.UnitTests.Attendance;

public class InitializeAttendanceSheetQueryValidatorTests
{
    private readonly InitializeAttendanceSheetQueryValidator _validator = new();

    private static InitializeAttendanceSheetQuery Valid() => new()
    {
        ClassroomId = Guid.NewGuid(),
        SubjectId = Guid.NewGuid(),
        Date = DateOnly.FromDateTime(DateTime.UtcNow),
        Period = "Matin"
    };

    [Fact]
    public void Valid_Query_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_ClassroomId_Should_Fail()
    {
        _validator.Validate(Valid() with { ClassroomId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_SubjectId_Should_Fail()
    {
        _validator.Validate(Valid() with { SubjectId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_Period_Should_Fail()
    {
        _validator.Validate(Valid() with { Period = "" }).IsValid.Should().BeFalse();
    }
}
