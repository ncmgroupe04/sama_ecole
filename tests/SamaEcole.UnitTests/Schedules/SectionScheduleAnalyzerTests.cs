using FluentAssertions;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.HourVolumes;
using SamaEcole.Application.ReportCards;
using Xunit;

namespace SamaEcole.UnitTests.Schedules;

/// <summary>
/// Alternance français / arabe d'un emploi du temps : un CONSTAT (jamais un refus). Le refus des chevauchements
/// reste celui de <see cref="TimetableCompliance.DetectConflicts"/> — un enseignant bilingue ne peut pas plus
/// être à deux endroits à la fois que les autres.
/// </summary>
public class SectionScheduleAnalyzerTests
{
    private static SectionSlot Fr(DayOfWeek day, int startHour, int endHour) =>
        new(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), ReportSection.French);

    private static SectionSlot Ar(DayOfWeek day, int startHour, int endHour) =>
        new(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), ReportSection.Arabic);

    private static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    [Fact]
    public void An_Empty_Timetable_Has_No_Pattern()
    {
        var result = SectionScheduleAnalyzer.Analyze([]);

        result.Pattern.Should().Be(SectionPattern.NoSlots);
        result.FrenchHours.Should().Be(0);
        result.ArabicHours.Should().Be(0);
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void A_Timetable_Of_One_Section_Is_Reported_As_Single_Section()
    {
        var result = SectionScheduleAnalyzer.Analyze([Fr(DayOfWeek.Monday, 8, 10), Fr(DayOfWeek.Tuesday, 8, 10)]);

        result.Pattern.Should().Be(SectionPattern.SingleSection);
        result.CoversBothSections.Should().BeFalse();
    }

    [Fact]
    public void Morning_French_And_Afternoon_Arabic_Every_Day_Is_Recognised()
    {
        var slots = Week.SelectMany(d => new[] { Fr(d, 8, 12), Ar(d, 14, 17) });

        var result = SectionScheduleAnalyzer.Analyze(slots);

        result.Pattern.Should().Be(SectionPattern.MorningFrenchAfternoonArabic);
        result.FrenchHours.Should().Be(16);
        result.ArabicHours.Should().Be(12);
        result.CoversBothSections.Should().BeTrue();
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Morning_Arabic_And_Afternoon_French_Every_Day_Is_Recognised()
    {
        var slots = Week.SelectMany(d => new[] { Ar(d, 8, 11), Fr(d, 15, 17) });

        SectionScheduleAnalyzer.Analyze(slots).Pattern.Should().Be(SectionPattern.MorningArabicAfternoonFrench);
    }

    [Fact]
    public void Whole_Days_Alternating_Between_The_Sections_Are_Recognised()
    {
        SectionSlot[] slots =
        [
            Fr(DayOfWeek.Monday, 8, 12), Fr(DayOfWeek.Monday, 14, 16),
            Ar(DayOfWeek.Tuesday, 8, 12), Ar(DayOfWeek.Tuesday, 14, 16),
            Fr(DayOfWeek.Wednesday, 8, 12),
            Ar(DayOfWeek.Thursday, 8, 12)
        ];

        SectionScheduleAnalyzer.Analyze(slots).Pattern.Should().Be(SectionPattern.AlternatingDays);
    }

    [Fact]
    public void A_Half_Day_Shared_By_Both_Sections_Is_Mixed_And_Is_Pointed_Out()
    {
        SectionSlot[] slots = [Fr(DayOfWeek.Monday, 8, 10), Ar(DayOfWeek.Monday, 10, 12), Ar(DayOfWeek.Monday, 14, 16)];

        var result = SectionScheduleAnalyzer.Analyze(slots);

        result.Pattern.Should().Be(SectionPattern.Mixed);
        result.Notices.Should().ContainSingle().Which.Should().Be("Lundi matin : les deux sections se partagent la demi-journée.");
    }

    [Fact]
    public void An_Irregular_Alternation_Is_Mixed_Without_Any_Notice()
    {
        // Lundi : français le matin / arabe l'après-midi ; mardi : l'inverse. Aucune demi-journée n'est partagée.
        SectionSlot[] slots = [Fr(DayOfWeek.Monday, 8, 12), Ar(DayOfWeek.Monday, 14, 16), Ar(DayOfWeek.Tuesday, 8, 12), Fr(DayOfWeek.Tuesday, 14, 16)];

        var result = SectionScheduleAnalyzer.Analyze(slots);

        result.Pattern.Should().Be(SectionPattern.Mixed);
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void A_Day_With_Only_One_Half_Breaks_The_Every_Day_Split_Pattern()
    {
        // Mercredi n'a qu'une demi-journée : le motif « matin français / après-midi arabe » ne vaut plus pour
        // TOUS les jours, et l'alternance par journées ne vaut pas non plus (lundi mélange les sections).
        SectionSlot[] slots = [Fr(DayOfWeek.Monday, 8, 12), Ar(DayOfWeek.Monday, 14, 16), Fr(DayOfWeek.Wednesday, 8, 12)];

        SectionScheduleAnalyzer.Analyze(slots).Pattern.Should().Be(SectionPattern.Mixed);
    }

    [Fact]
    public void A_Course_Starting_Before_One_O_Clock_Belongs_To_The_Morning_Even_If_It_Ends_After()
    {
        var result = SectionScheduleAnalyzer.Analyze([
            new SectionSlot(DayOfWeek.Monday, new TimeOnly(11, 30), new TimeOnly(13, 30), ReportSection.French)]);

        var half = result.HalfDays.Should().ContainSingle().Subject;
        half.Half.Should().Be(DayHalf.Morning);
        half.FrenchHours.Should().Be(2);
    }

    [Theory]
    [InlineData(12, 59, DayHalf.Morning)]
    [InlineData(13, 0, DayHalf.Afternoon)]
    [InlineData(8, 0, DayHalf.Morning)]
    [InlineData(17, 30, DayHalf.Afternoon)]
    public void The_Half_Day_Is_Decided_By_The_Start_Time(int hour, int minute, DayHalf expected)
    {
        SectionScheduleAnalyzer.HalfOf(new TimeOnly(hour, minute)).Should().Be(expected);
    }

    [Fact]
    public void The_Afternoon_Start_Can_Be_Overridden()
    {
        SectionScheduleAnalyzer.HalfOf(new TimeOnly(12, 30), new TimeOnly(12, 0)).Should().Be(DayHalf.Afternoon);
    }

    [Fact]
    public void Hours_Are_Summed_Per_Section_In_Real_Hours_Not_In_Slot_Counts()
    {
        SectionSlot[] slots =
        [
            new(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(9, 30), ReportSection.French),
            new(DayOfWeek.Monday, new TimeOnly(9, 30), new TimeOnly(10, 0), ReportSection.French),
            new(DayOfWeek.Tuesday, new TimeOnly(15, 0), new TimeOnly(16, 45), ReportSection.Arabic)
        ];

        var result = SectionScheduleAnalyzer.Analyze(slots);

        result.FrenchHours.Should().Be(2m);
        result.ArabicHours.Should().Be(1.75m);
    }

    [Fact]
    public void A_Slot_Without_Duration_Is_Ignored()
    {
        var result = SectionScheduleAnalyzer.Analyze([Fr(DayOfWeek.Monday, 8, 8)]);

        result.Pattern.Should().Be(SectionPattern.NoSlots);
    }

    [Fact]
    public void Half_Days_Come_Back_In_Week_Order_Morning_Before_Afternoon()
    {
        var result = SectionScheduleAnalyzer.Analyze([
            Ar(DayOfWeek.Tuesday, 14, 16), Fr(DayOfWeek.Monday, 14, 16), Fr(DayOfWeek.Monday, 8, 10)]);

        result.HalfDays.Select(h => (h.Day, h.Half)).Should().Equal(
            (DayOfWeek.Monday, DayHalf.Morning), (DayOfWeek.Monday, DayHalf.Afternoon), (DayOfWeek.Tuesday, DayHalf.Afternoon));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, "Lundi")]
    [InlineData(DayOfWeek.Saturday, "Samedi")]
    [InlineData(DayOfWeek.Sunday, "Dimanche")]
    public void Day_Names_Are_French_Whatever_The_Server_Culture(DayOfWeek day, string expected)
    {
        SectionScheduleAnalyzer.DayName(day).Should().Be(expected);
    }

    // ------------------------------------------------------- Détection de conflits : bilingue = comme les autres

    private static SlotInfo Slot(Guid teacher, Guid classroom, int startHour, int endHour, string? room = null) =>
        new(Guid.NewGuid(), DayOfWeek.Monday, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0),
            teacher, "Enseignant", classroom, "Classe", Guid.NewGuid(), "Matière", room);

    [Fact]
    public void A_Bilingual_Teacher_Cannot_Teach_Two_Sections_At_The_Same_Time()
    {
        var teacher = Guid.NewGuid();

        var conflicts = TimetableCompliance.DetectConflicts([
            Slot(teacher, Guid.NewGuid(), 8, 10),    // section française, classe A
            Slot(teacher, Guid.NewGuid(), 9, 11)]);  // section arabe, classe B

        conflicts.Should().ContainSingle().Which.Kind.Should().Be(ScheduleConflictKind.Teacher);
    }

    [Fact]
    public void A_Bilingual_Teacher_Moving_From_One_Section_To_The_Next_Without_Overlap_Has_No_Conflict()
    {
        var teacher = Guid.NewGuid();

        TimetableCompliance.DetectConflicts([
            Slot(teacher, Guid.NewGuid(), 8, 10),
            Slot(teacher, Guid.NewGuid(), 10, 12)]).Should().BeEmpty("deux créneaux qui se touchent ne se chevauchent pas");
    }

    [Fact]
    public void Two_Sections_Of_The_Same_Class_Cannot_Share_A_Slot()
    {
        var classroom = Guid.NewGuid();

        var conflicts = TimetableCompliance.DetectConflicts([
            Slot(Guid.NewGuid(), classroom, 8, 10),
            Slot(Guid.NewGuid(), classroom, 9, 10)]);

        conflicts.Should().ContainSingle().Which.Kind.Should().Be(ScheduleConflictKind.Classroom);
    }

    [Fact]
    public void The_Same_Room_Used_By_Both_Sections_At_The_Same_Time_Is_A_Room_Conflict()
    {
        var conflicts = TimetableCompliance.DetectConflicts([
            Slot(Guid.NewGuid(), Guid.NewGuid(), 8, 10, "Salle 12"),
            Slot(Guid.NewGuid(), Guid.NewGuid(), 9, 11, "salle-12")]);

        conflicts.Should().ContainSingle().Which.Kind.Should().Be(ScheduleConflictKind.Room);
    }
}
