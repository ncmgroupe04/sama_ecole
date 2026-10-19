using FluentAssertions;
using SamaEcole.Application.RollBook;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.RollBook;

/// <summary>Cahier de roulement (Franco-Arabe Bloc 5b) : dérivation d'une demi-journée, fusion saisie/calcul, calendrier, taux.</summary>
public class RollBookLogicTests
{
    private static SessionLine S(AttendanceStatus status, int lateMinutes = 0) => new(status, lateMinutes);

    // -- HalfDayDeriver --------------------------------------------------------------------------------

    [Fact]
    public void No_Session_Gives_An_Empty_Cell()
        => HalfDayDeriver.Derive([]).Should().Be(RollBookCell.Empty);

    [Fact]
    public void All_Present_Sessions_Give_Present()
    {
        var cell = HalfDayDeriver.Derive([S(AttendanceStatus.Present), S(AttendanceStatus.Present)]);

        cell.Mark.Should().Be(RollBookMark.Present);
        cell.Source.Should().Be(RollBookSource.Derived);
    }

    [Fact]
    public void A_Late_Session_Without_Absence_Gives_Late_With_The_Longest_Delay()
    {
        var cell = HalfDayDeriver.Derive([S(AttendanceStatus.Late, 10), S(AttendanceStatus.Present), S(AttendanceStatus.Late, 25)]);

        cell.Mark.Should().Be(RollBookMark.Late);
        cell.LateMinutes.Should().Be(25);
    }

    [Fact]
    public void Absent_To_Some_Sessions_Only_Gives_Partial_Absence()
        => HalfDayDeriver.Derive([S(AttendanceStatus.Present), S(AttendanceStatus.UnjustifiedAbsence)]).Mark
            .Should().Be(RollBookMark.PartialAbsence);

    [Fact]
    public void Absent_To_Every_Session_Is_Justified_Only_If_Every_Session_Is()
    {
        HalfDayDeriver.Derive([S(AttendanceStatus.JustifiedAbsence), S(AttendanceStatus.JustifiedAbsence)]).Mark
            .Should().Be(RollBookMark.JustifiedAbsence);

        HalfDayDeriver.Derive([S(AttendanceStatus.JustifiedAbsence), S(AttendanceStatus.UnjustifiedAbsence)]).Mark
            .Should().Be(RollBookMark.UnjustifiedAbsence, "une seule séance non justifiée suffit à ne pas excuser la demi-journée");
    }

    // -- RollBookCellResolver --------------------------------------------------------------------------

    [Fact]
    public void An_Entry_Takes_Precedence_Over_The_Derived_Status()
    {
        var entry = new RollBookEntry
        {
            Id = Guid.NewGuid(), Status = AttendanceStatus.JustifiedAbsence, Reason = "Certificat médical"
        };
        var derived = new RollBookCell(RollBookMark.Present, RollBookSource.Derived);

        var cell = RollBookCellResolver.Resolve(entry, derived);

        cell.Mark.Should().Be(RollBookMark.JustifiedAbsence);
        cell.Source.Should().Be(RollBookSource.Entry);
        cell.Reason.Should().Be("Certificat médical");
        cell.EntryId.Should().Be(entry.Id);
    }

    [Fact]
    public void Without_An_Entry_The_Derived_Status_Is_Kept()
    {
        var derived = new RollBookCell(RollBookMark.Late, RollBookSource.Derived, 12);

        RollBookCellResolver.Resolve(null, derived).Should().Be(derived);
        RollBookCellResolver.Resolve(null, RollBookCell.Empty).IsRecorded.Should().BeFalse();
    }

    [Theory]
    [InlineData(AttendanceStatus.Present, RollBookMark.Present)]
    [InlineData(AttendanceStatus.Late, RollBookMark.Late)]
    [InlineData(AttendanceStatus.JustifiedAbsence, RollBookMark.JustifiedAbsence)]
    [InlineData(AttendanceStatus.UnjustifiedAbsence, RollBookMark.UnjustifiedAbsence)]
    public void Marks_And_Statuses_Round_Trip(AttendanceStatus status, RollBookMark mark)
    {
        RollBookMarks.MarkOf(status).Should().Be(mark);
        RollBookMarks.StatusOf(mark).Should().Be(status);
    }

    [Fact]
    public void Partial_Absence_And_Not_Recorded_Cannot_Be_Entered()
    {
        RollBookMarks.StatusOf(RollBookMark.PartialAbsence).Should().BeNull();
        RollBookMarks.StatusOf(RollBookMark.NotRecorded).Should().BeNull();
    }

    // -- RollBookCalendar ------------------------------------------------------------------------------

    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    private static SchoolClosure Closure(DateOnly start, DateOnly end, string label = "Fermeture") =>
        new() { Label = label, StartDate = start, EndDate = end, Kind = SchoolClosureKind.Holiday };

    [Fact]
    public void Class_Days_Exclude_Rest_Days_And_Closures()
    {
        // Novembre 2026 : le 1er est un dimanche ; fermeture du jeudi 5 au vendredi 6.
        var days = RollBookCalendar.Build(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 8), MondayToSaturday,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 7, 31),
            [Closure(new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6), "Vacances")]);

        days.Should().HaveCount(8);
        days[0].OffReason.Should().Be(DayOffReason.RestDay, "dimanche");
        days[1].IsClassDay.Should().BeTrue("lundi 2");
        days[4].OffReason.Should().Be(DayOffReason.Closure);
        days[4].Closure!.Label.Should().Be("Vacances");
        days[5].OffReason.Should().Be(DayOffReason.Closure, "vendredi 6, dernier jour fermé (borne incluse)");
        days[6].IsClassDay.Should().BeTrue("samedi 7");
        days[7].OffReason.Should().Be(DayOffReason.RestDay, "dimanche 8");
        days.Count(d => d.IsClassDay).Should().Be(4);
    }

    [Fact]
    public void A_Rest_Day_Wins_Over_A_Closure_That_Covers_It()
    {
        var days = RollBookCalendar.Build(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), MondayToSaturday,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 7, 31),
            [Closure(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 3))]);

        days.Single().OffReason.Should().Be(DayOffReason.RestDay);
    }

    [Fact]
    public void Days_Outside_The_School_Year_Are_Not_Class_Days()
    {
        var days = RollBookCalendar.Build(
            new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2), MondayToSaturday,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 7, 31), []);

        days.Where(d => d.OffReason == DayOffReason.OutsideYear).Select(d => d.Date.Day).Should().Equal(28, 29, 30);
        days.Where(d => d.IsClassDay).Select(d => d.Date.Day).Should().Equal(1, 2);
    }

    // -- AttendanceCounts ------------------------------------------------------------------------------

    private static RollBookCell Cell(RollBookMark mark, int minutes = 0) => new(mark, RollBookSource.Entry, minutes);

    [Fact]
    public void Rates_Follow_The_Validated_Formulas()
    {
        // 10 renseignées : 5 présents, 1 retard, 1 partielle, 2 justifiées, 1 non justifiée.
        var counts = AttendanceCounts.From(
        [
            ..Enumerable.Repeat(Cell(RollBookMark.Present), 5),
            Cell(RollBookMark.Late, 15),
            Cell(RollBookMark.PartialAbsence),
            ..Enumerable.Repeat(Cell(RollBookMark.JustifiedAbsence), 2),
            Cell(RollBookMark.UnjustifiedAbsence)
        ]);

        counts.Recorded.Should().Be(10);
        counts.LateMinutes.Should().Be(15);
        counts.PresenceRate.Should().Be(0.7m, "(présents + retards + partielles) ÷ renseignées = 7/10");
        counts.AbsenceRate.Should().Be(0.3m, "(justifiées + non justifiées) ÷ renseignées = 3/10");
        counts.AssiduityRate.Should().Be(0.9m, "seules les absences non justifiées pénalisent : 9/10");
    }

    [Fact]
    public void Not_Recorded_Cells_Count_For_Nothing()
    {
        var counts = AttendanceCounts.From([Cell(RollBookMark.NotRecorded), Cell(RollBookMark.Present)]);

        counts.Recorded.Should().Be(1);
        counts.PresenceRate.Should().Be(1m);
    }

    [Fact]
    public void Without_Any_Recorded_Cell_The_Rates_Are_Null_Not_A_Fake_Hundred_Percent()
    {
        var counts = AttendanceCounts.Zero;

        counts.PresenceRate.Should().BeNull();
        counts.AbsenceRate.Should().BeNull();
        counts.AssiduityRate.Should().BeNull();
        counts.Coverage(20).Should().Be(0m);
    }

    [Fact]
    public void Coverage_Is_The_Recorded_Share_Of_The_Expected_Half_Days_And_Never_Exceeds_One()
    {
        new AttendanceCounts(Present: 5, Late: 5).Coverage(20).Should().Be(0.5m);
        new AttendanceCounts(Present: 30).Coverage(20).Should().Be(1m, "des saisies hors jours attendus ne dépassent pas 100 %");
        new AttendanceCounts(Present: 3).Coverage(0).Should().BeNull();
    }

    [Fact]
    public void A_Level_Sums_Counts_Instead_Of_Averaging_Rates()
    {
        // Classe de 2 élèves à 100 % et classe de 8 élèves à 50 % : la moyenne des taux dirait 75 %, la vérité est 60 %.
        var small = new AttendanceCounts(Present: 2);
        var large = new AttendanceCounts(Present: 4, UnjustifiedAbsence: 4);

        AttendanceCounts.Sum([small, large]).PresenceRate.Should().Be(0.6m);
    }

    [Fact]
    public void Plus_Adds_Every_Counter()
    {
        var total = new AttendanceCounts(1, 2, 3, 4, 5, 6).Plus(new AttendanceCounts(10, 20, 30, 40, 50, 60));

        total.Should().Be(new AttendanceCounts(11, 22, 33, 44, 55, 66));
    }

    // -- RollBookPeriods -------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-11", "2026-11-01", "2026-11-30")]
    [InlineData("2027-02", "2027-02-01", "2027-02-28")]
    [InlineData("2028-02", "2028-02-01", "2028-02-29")]
    [InlineData("2026-12", "2026-12-01", "2026-12-31")]
    public void A_Month_Gives_Its_First_And_Last_Day(string month, string first, string last)
    {
        RollBookPeriods.TryParseMonth(month, out var f, out var l).Should().BeTrue();

        f.Should().Be(DateOnly.Parse(first));
        l.Should().Be(DateOnly.Parse(last));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("26-11")]
    [InlineData("2026-11-01")]
    [InlineData("novembre")]
    public void Anything_But_Yyyy_Mm_Is_Rejected(string? month)
        => RollBookPeriods.TryParseMonth(month, out _, out _).Should().BeFalse();
}
