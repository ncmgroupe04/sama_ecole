using FluentAssertions;
using SamaEcole.Application.Internat.Queries.GetProgressHistory;
using SamaEcole.Application.Internat.Queries.GetStudentHizbHistory;
using Xunit;

namespace SamaEcole.UnitTests.Internat;

public class HizbHistoryQueryValidatorTests
{
    private readonly GetStudentHizbHistoryQueryValidator _student = new();
    private readonly GetProgressHistoryQueryValidator _progress = new();

    [Fact]
    public void An_Open_Period_Is_Valid()
    {
        _student.Validate(new GetStudentHizbHistoryQuery(Guid.NewGuid())).IsValid.Should().BeTrue();
        _progress.Validate(new GetProgressHistoryQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Period_That_Ends_Before_It_Starts_Fails()
    {
        var from = new DateOnly(2026, 10, 10);
        var to = new DateOnly(2026, 10, 9);

        _student.Validate(new GetStudentHizbHistoryQuery(Guid.NewGuid(), from, to)).IsValid.Should().BeFalse();
        _progress.Validate(new GetProgressHistoryQuery(from, to)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_One_Day_Period_Is_Valid()
    {
        var day = new DateOnly(2026, 10, 10);

        _student.Validate(new GetStudentHizbHistoryQuery(Guid.NewGuid(), day, day)).IsValid.Should().BeTrue();
        _progress.Validate(new GetProgressHistoryQuery(day, day)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Student_Or_Instructor_Identifier_Fails()
    {
        _student.Validate(new GetStudentHizbHistoryQuery(Guid.Empty)).IsValid.Should().BeFalse();
        _progress.Validate(new GetProgressHistoryQuery(InstructorId: Guid.Empty)).IsValid.Should().BeFalse();
    }
}
