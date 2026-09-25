using FluentAssertions;
using SamaEcole.Application.Attendance;
using Xunit;
using S = SamaEcole.Domain.Enums.AttendanceStatus;

namespace SamaEcole.UnitTests.Attendance;

public class DayAttendanceClassifierTests
{
    [Fact]
    public void Absent_To_Every_Recorded_Session_Is_A_Full_Absence()
        => DayAttendanceClassifier.Classify([S.UnjustifiedAbsence, S.JustifiedAbsence, S.UnjustifiedAbsence])
            .Should().Be(DayAttendanceKind.FullAbsence);

    [Fact]
    public void Absent_To_One_Session_But_Present_To_Another_Is_A_Partial_Absence()
        => DayAttendanceClassifier.Classify([S.Present, S.UnjustifiedAbsence])
            .Should().Be(DayAttendanceKind.PartialAbsence);

    [Fact]
    public void Absent_Then_Late_Counts_As_Partial_Because_The_Student_Did_Come()
        => DayAttendanceClassifier.Classify([S.UnjustifiedAbsence, S.Late])
            .Should().Be(DayAttendanceKind.PartialAbsence);

    [Fact]
    public void A_Late_Without_Any_Absence_Stays_A_Late()
        => DayAttendanceClassifier.Classify([S.Present, S.Late]).Should().Be(DayAttendanceKind.Late);

    [Fact]
    public void All_Present_Is_Present()
        => DayAttendanceClassifier.Classify([S.Present, S.Present]).Should().Be(DayAttendanceKind.Present);

    [Fact]
    public void No_Recorded_Session_Is_Not_An_Absence()
        => DayAttendanceClassifier.Classify([]).Should().Be(DayAttendanceKind.Present);

    // La forme par COMPTEURS (celle du rapport, agrégée côté base) suit exactement la même règle.
    [Theory]
    [InlineData(0, 0, 0, DayAttendanceKind.Present)]
    [InlineData(3, 3, 0, DayAttendanceKind.FullAbsence)]
    [InlineData(3, 1, 0, DayAttendanceKind.PartialAbsence)]
    [InlineData(2, 1, 1, DayAttendanceKind.PartialAbsence)]
    [InlineData(3, 0, 1, DayAttendanceKind.Late)]
    [InlineData(3, 0, 0, DayAttendanceKind.Present)]
    public void The_Counter_Form_Follows_The_Same_Rule(int sessions, int absences, int lates, DayAttendanceKind expected)
        => DayAttendanceClassifier.Classify(sessions, absences, lates).Should().Be(expected);

    [Fact]
    public void A_Single_Late_Session_Is_A_Late_Not_An_Absence()
        => DayAttendanceClassifier.Classify([S.Late]).Should().Be(DayAttendanceKind.Late);
}
