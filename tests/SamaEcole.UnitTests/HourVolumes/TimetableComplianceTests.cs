using FluentAssertions;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.HourVolumes;
using Xunit;

namespace SamaEcole.UnitTests.HourVolumes;

/// <summary>Évolution N°7 — grilles horaires de référence, écarts d'un emploi du temps et détection des chevauchements.</summary>
public class TimetableComplianceTests
{
    // ------------------------------------------------------------------ Grilles de référence

    [Theory]
    [InlineData("Sixième", null, "Français", 6)]
    [InlineData("Troisième", null, "Maths", 5)]
    [InlineData("Troisième", null, "Sciences physiques", 3)]
    [InlineData("Terminale", "S1", "Mathématiques", 8)]
    [InlineData("Terminale", "S2", "Sciences de la vie et de la terre", 6)]
    [InlineData("Terminale", "L2", "Philo", 7)]
    public void Reference_Volumes_Are_Found_Under_Common_Subject_Names(string grade, string? series, string subject, decimal hours)
        => WeeklyHourTemplates.LineFor(grade, series, subject)!.Hours.Should().Be(hours);

    [Fact]
    public void Seconde_Falls_Back_On_The_Series_Family()
    {
        WeeklyHourTemplates.For("Seconde", "S2").Should().BeSameAs(WeeklyHourTemplates.For("Seconde", "S1"));
        WeeklyHourTemplates.LineFor("Seconde", "L2", "Français")!.Hours.Should().Be(5);
    }

    [Theory]
    [InlineData("CM2", null)]
    [InlineData("Terminale", null)]
    [InlineData("Terminale", "STEG")]
    [InlineData(null, "S1")]
    public void No_Grid_Is_Invented(string? grade, string? series)
        => WeeklyHourTemplates.For(grade, series).Should().BeEmpty();

    [Fact]
    public void Second_Languages_Are_Alternatives_Of_One_Group()
    {
        var lv2 = WeeklyHourTemplates.For("Troisième", null).Where(l => l.OptionGroup == WeeklyHourTemplates.SecondLanguageGroup).ToList();

        lv2.Select(l => l.Label).Should().Contain(["Espagnol", "Arabe", "Allemand"]);
        lv2.Select(l => l.Hours).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Every_Grid_Is_Well_Formed()
    {
        foreach (var grade in new[] { "Sixième", "Cinquième", "Quatrième", "Troisième", "Seconde", "Première", "Terminale" })
        {
            foreach (var series in new string?[] { null }.Concat(LyceeSeries.All.Select(s => s.Code)))
            {
                var lines = WeeklyHourTemplates.For(grade, series);
                if (lines.Count == 0) continue;
                lines.Should().OnlyContain(l => l.Hours > 0 && l.Hours <= HourVolumeRulesMax);
                lines.Select(l => SeriesCoefficientTemplates.NormalizeName(l.Label)).Should().OnlyHaveUniqueItems($"{grade} {series}");

                // Un alias ne désigne jamais deux lignes d'une même grille.
                foreach (var line in lines)
                {
                    foreach (var alias in line.Aliases)
                    {
                        lines.Count(l => l.Matches(alias)).Should().Be(1, $"« {alias} » ({grade} {series})");
                    }
                }
            }
        }
    }

    private const decimal HourVolumeRulesMax = 40m;

    // ------------------------------------------------------------------ Écarts

    private static readonly Guid Maths = Guid.NewGuid();
    private static readonly Guid Francais = Guid.NewGuid();
    private static readonly Guid Espagnol = Guid.NewGuid();
    private static readonly Guid Arabe = Guid.NewGuid();
    private static readonly Guid Club = Guid.NewGuid();

    [Theory]
    [InlineData(5, 5, HourComplianceStatus.Compliant)]
    [InlineData(4, 5, HourComplianceStatus.Under)]
    [InlineData(6, 5, HourComplianceStatus.Over)]
    [InlineData(3, null, HourComplianceStatus.NoReference)]
    public void Status_Compares_Planned_And_Reference(decimal planned, int? norm, HourComplianceStatus expected)
        => TimetableCompliance.StatusOf(planned, norm).Should().Be(expected);

    [Fact]
    public void Less_Than_A_Minute_Of_Rounding_Is_Compliant()
        => TimetableCompliance.StatusOf(TimetableCompliance.HoursOf(new TimeOnly(8, 0), new TimeOnly(9, 55)) * 3m - 0.001m, 5.75m)
            .Should().Be(HourComplianceStatus.Compliant);

    [Fact]
    public void A_Reference_Subject_Without_Slot_Is_Under_And_A_Subject_Without_Reference_Stays_Unjudged()
    {
        var rows = TimetableCompliance.Evaluate(
        [
            new ComplianceInput(Maths, "Mathématiques", 0m, 5m, null),
            new ComplianceInput(Club, "Club théâtre", 2m, null, null),
            new ComplianceInput(Francais, "Français", 0m, null, null)
        ]);

        rows.Should().HaveCount(2, "une matière sans créneau ni référence n'a rien à dire");
        rows.Single(r => r.SubjectId == Maths).Status.Should().Be(HourComplianceStatus.Under);
        rows.Single(r => r.SubjectId == Maths).Difference.Should().Be(-5m);
        rows.Single(r => r.SubjectId == Club).Status.Should().Be(HourComplianceStatus.NoReference);
    }

    [Fact]
    public void Unscheduled_Alternatives_Of_A_Scheduled_Group_Are_Ignored()
    {
        var rows = TimetableCompliance.Evaluate(
        [
            new ComplianceInput(Espagnol, "Espagnol", 3m, 3m, "LV2"),
            new ComplianceInput(Arabe, "Arabe", 0m, 3m, "LV2")
        ]);

        rows.Should().ContainSingle().Which.SubjectId.Should().Be(Espagnol);
        rows[0].Status.Should().Be(HourComplianceStatus.Compliant);
    }

    [Fact]
    public void A_Group_With_No_Scheduled_Alternative_Is_Reported_Once()
    {
        var rows = TimetableCompliance.Evaluate(
        [
            new ComplianceInput(Espagnol, "Espagnol", 0m, 3m, "LV2"),
            new ComplianceInput(Arabe, "Arabe", 0m, 3m, "LV2")
        ]);

        var row = rows.Should().ContainSingle().Subject;
        row.SubjectId.Should().BeNull();
        row.SubjectName.Should().Be("LV2 (au choix)");
        row.Status.Should().Be(HourComplianceStatus.Under);
    }

    [Fact]
    public void Class_Total_Counts_A_Group_Once()
    {
        var totals = TimetableCompliance.Totals(
        [
            new ComplianceInput(Maths, "Mathématiques", 5m, 5m, null),
            new ComplianceInput(Espagnol, "Espagnol", 3m, 3m, "LV2"),
            new ComplianceInput(Arabe, "Arabe", 3m, 3m, "LV2"),
            new ComplianceInput(Club, "Club", 1m, null, null)
        ]);

        totals.NormHours.Should().Be(8m, "5 h de maths + 3 h de LV2, quelle que soit la langue");
        totals.PlannedHours.Should().Be(12m);
        totals.Status.Should().Be(HourComplianceStatus.Over);
    }

    [Fact]
    public void A_Class_Without_Any_Reference_Has_No_Total_Reference()
        => TimetableCompliance.Totals([new ComplianceInput(Club, "Club", 2m, null, null)]).Status
            .Should().Be(HourComplianceStatus.NoReference);

    // ------------------------------------------------------------------ Chevauchements

    private static readonly Guid ProfA = Guid.NewGuid();
    private static readonly Guid ProfB = Guid.NewGuid();
    private static readonly Guid ClasseA = Guid.NewGuid();
    private static readonly Guid ClasseB = Guid.NewGuid();

    private static SlotInfo Slot(DayOfWeek day, int start, int end, Guid teacher, Guid classroom, string? room)
        => new(Guid.NewGuid(), day, new TimeOnly(start, 0), new TimeOnly(end, 0), teacher, teacher == ProfA ? "A" : "B",
            classroom, classroom == ClasseA ? "6e A" : "6e B", Guid.NewGuid(), "Maths", room);

    [Fact]
    public void Same_Teacher_At_Overlapping_Times_Is_A_Conflict()
    {
        var conflicts = TimetableCompliance.DetectConflicts(
        [
            Slot(DayOfWeek.Monday, 8, 10, ProfA, ClasseA, null),
            Slot(DayOfWeek.Monday, 9, 11, ProfA, ClasseB, null)
        ]);

        conflicts.Should().ContainSingle().Which.Kind.Should().Be(ScheduleConflictKind.Teacher);
    }

    [Fact]
    public void Same_Room_Is_Compared_Without_Case_Accents_Or_Punctuation()
    {
        var conflicts = TimetableCompliance.DetectConflicts(
        [
            Slot(DayOfWeek.Tuesday, 8, 10, ProfA, ClasseA, "Salle 12"),
            Slot(DayOfWeek.Tuesday, 9, 10, ProfB, ClasseB, "salle-12")
        ]);

        var conflict = conflicts.Should().ContainSingle().Subject;
        conflict.Kind.Should().Be(ScheduleConflictKind.Room);
        conflict.Resource.Should().Be("Salle 12");
    }

    [Fact]
    public void Adjacent_Slots_Different_Days_And_Empty_Rooms_Are_Not_Conflicts()
    {
        TimetableCompliance.DetectConflicts(
        [
            Slot(DayOfWeek.Monday, 8, 10, ProfA, ClasseA, "   "),
            Slot(DayOfWeek.Monday, 10, 12, ProfA, ClasseA, "Salle 1"),
            Slot(DayOfWeek.Monday, 8, 10, ProfB, ClasseB, null),
            Slot(DayOfWeek.Wednesday, 10, 12, ProfA, ClasseA, "Salle 1")
        ]).Should().BeEmpty();
    }

    [Fact]
    public void Two_Shared_Resources_Give_Two_Conflicts()
    {
        TimetableCompliance.DetectConflicts(
        [
            Slot(DayOfWeek.Friday, 8, 10, ProfA, ClasseA, "Labo"),
            Slot(DayOfWeek.Friday, 8, 9, ProfA, ClasseA, "LABO")
        ]).Select(c => c.Kind).Should().BeEquivalentTo(
            [ScheduleConflictKind.Teacher, ScheduleConflictKind.Room, ScheduleConflictKind.Classroom]);
    }

    [Fact]
    public void A_Long_Slot_Overlaps_Every_Later_Slot_It_Covers()
        => TimetableCompliance.DetectConflicts(
        [
            Slot(DayOfWeek.Thursday, 8, 12, ProfA, ClasseA, null),
            Slot(DayOfWeek.Thursday, 9, 10, ProfB, ClasseB, null),
            Slot(DayOfWeek.Thursday, 11, 12, ProfA, ClasseB, null)
        ]).Should().ContainSingle(c => c.Kind == ScheduleConflictKind.Teacher);
}
