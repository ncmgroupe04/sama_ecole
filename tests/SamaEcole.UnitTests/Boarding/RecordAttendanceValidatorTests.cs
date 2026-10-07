using FluentAssertions;
using SamaEcole.Application.Boarding.Attendance;
using SamaEcole.Application.Boarding.Attendance.RecordAttendance;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class RecordAttendanceValidatorTests
{
    private readonly RecordAttendanceCommandValidator _validator = new();

    private static RecordAttendanceCommand Valid(
        Guid? dormitory = null, DateOnly? date = null, IReadOnlyList<AttendanceEntryInput>? entries = null) =>
        new(date ?? new DateOnly(2026, 10, 11), dormitory ?? Guid.NewGuid(),
            entries ?? [new AttendanceEntryInput(Guid.NewGuid(), true, null)]);

    [Fact] public void A_Complete_Sheet_Passes() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact] public void A_Missing_Dormitory_Is_Refused() => _validator.Validate(Valid(dormitory: Guid.Empty)).IsValid.Should().BeFalse();

    [Fact] public void A_Missing_Date_Is_Refused() => _validator.Validate(Valid(date: default(DateOnly))).IsValid.Should().BeFalse();

    [Fact] public void An_Empty_Sheet_Is_Refused() => _validator.Validate(Valid(entries: [])).IsValid.Should().BeFalse();

    [Fact]
    public void More_Than_200_Lines_Are_Refused()
    {
        var entries = Enumerable.Range(0, 201).Select(_ => new AttendanceEntryInput(Guid.NewGuid(), true, null)).ToList();

        _validator.Validate(Valid(entries: entries)).IsValid.Should().BeFalse();
        _validator.Validate(Valid(entries: entries.Take(200).ToList())).IsValid.Should().BeTrue();
    }

    [Fact] public void A_Line_Without_A_Boarder_Is_Refused() =>
        _validator.Validate(Valid(entries: [new AttendanceEntryInput(Guid.Empty, true, null)])).IsValid.Should().BeFalse();

    [Fact] public void A_Note_Longer_Than_300_Characters_Is_Refused() =>
        _validator.Validate(Valid(entries: [new AttendanceEntryInput(Guid.NewGuid(), false, new string('a', 301))])).IsValid.Should().BeFalse();
}
