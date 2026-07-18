using FluentAssertions;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Attendance;

public class SubmitAttendanceSheetCommandValidatorTests
{
    private readonly SubmitAttendanceSheetCommandValidator _validator = new();

    private static SubmitAttendanceSheetCommand Valid(params AttendanceEntry[] entries) => new()
    {
        ClassroomId = Guid.NewGuid(),
        SubjectId = Guid.NewGuid(),
        Date = DateOnly.FromDateTime(DateTime.UtcNow),
        Period = "Matin",
        Entries = entries.Length > 0 ? entries : [new AttendanceEntry(Guid.NewGuid(), AttendanceStatus.Present, 0)]
    };

    [Fact]
    public void Valid_Sheet_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Entries_Should_Fail()
    {
        _validator.Validate(Valid() with { Entries = [] }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Future_Date_Should_Fail()
    {
        var command = Valid() with { Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_Period_Should_Fail()
    {
        _validator.Validate(Valid() with { Period = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Period_With_Html_Should_Fail()
    {
        // JGK-F01 — branchement de la règle NoHtml (payloads exhaustifs : SafeTextValidationTests).
        _validator.Validate(Valid() with { Period = "<b>Matin</b>" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Late_Without_Minutes_Should_Fail()
    {
        // Un retard doit indiquer un nombre de minutes strictement positif.
        var command = Valid(new AttendanceEntry(Guid.NewGuid(), AttendanceStatus.Late, 0));

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Late_With_Positive_Minutes_Should_Pass()
    {
        var command = Valid(new AttendanceEntry(Guid.NewGuid(), AttendanceStatus.Late, 15));

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Late_Beyond_The_Cap_Should_Fail()
    {
        var command = Valid(new AttendanceEntry(
            Guid.NewGuid(), AttendanceStatus.Late, SubmitAttendanceSheetCommandValidator.MaxLateMinutes + 1));

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Non_Late_Status_With_Minutes_Should_Fail()
    {
        // Les minutes de retard n'ont de sens que pour un statut « Retard ».
        var command = Valid(new AttendanceEntry(Guid.NewGuid(), AttendanceStatus.Present, 10));

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Duplicate_Student_Should_Fail()
    {
        var studentId = Guid.NewGuid();
        var command = Valid(
            new AttendanceEntry(studentId, AttendanceStatus.Present, 0),
            new AttendanceEntry(studentId, AttendanceStatus.UnjustifiedAbsence, 0));

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Several_Distinct_Students_Should_Pass()
    {
        var command = Valid(
            new AttendanceEntry(Guid.NewGuid(), AttendanceStatus.Present, 0),
            new AttendanceEntry(Guid.NewGuid(), AttendanceStatus.JustifiedAbsence, 0),
            new AttendanceEntry(Guid.NewGuid(), AttendanceStatus.Late, 5));

        _validator.Validate(command).IsValid.Should().BeTrue();
    }
}
