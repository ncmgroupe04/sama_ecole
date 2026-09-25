using FluentAssertions;
using SamaEcole.Application.Attendance;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Attendance;

public class SlotPeriodTests
{
    private static readonly Guid Classe = Guid.NewGuid();
    private static readonly Guid Matiere = Guid.NewGuid();

    private static ScheduleSlot Slot(DayOfWeek day = DayOfWeek.Saturday) => new()
    {
        ClassroomId = Classe, SubjectId = Matiere, DayOfWeek = day,
        StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
    };

    [Fact]
    public void Label_Is_Start_Dash_End_In_24h()
        => SlotPeriod.Label(new TimeOnly(8, 0), new TimeOnly(10, 30)).Should().Be("08:00-10:30");

    [Fact]
    public void Label_Uses_The_Afternoon_24h_Form()
        => SlotPeriod.Label(new TimeOnly(15, 5), new TimeOnly(16, 0)).Should().Be("15:05-16:00");

    [Fact]
    public void A_Matching_Slot_Has_No_Mismatch()
        => SlotPeriod.Mismatch(Slot(), Classe, Matiere, new DateOnly(2026, 9, 26)).Should().BeNull(); // samedi

    [Fact]
    public void A_Slot_Of_Another_Weekday_Is_Refused()
        => SlotPeriod.Mismatch(Slot(), Classe, Matiere, new DateOnly(2026, 9, 24)) // jeudi
            .Should().Contain("samedi");

    [Fact]
    public void A_Slot_Of_Another_Class_Or_Subject_Is_Refused()
    {
        SlotPeriod.Mismatch(Slot(), Guid.NewGuid(), Matiere, new DateOnly(2026, 9, 26)).Should().Contain("classe");
        SlotPeriod.Mismatch(Slot(), Classe, Guid.NewGuid(), new DateOnly(2026, 9, 26)).Should().Contain("matière");
    }
}
