using FluentAssertions;
using SamaEcole.Application.Boarding.Attendance.Sessions;
using SamaEcole.Application.Boarding.Documents.MonthlyAttendanceSummary;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// « Récapitulatif mensuel du pointage » imprimable — contrôlé sur le TEXTE réellement imprimé. Document de gestion (pension) : nom,
/// matricule et comptes, ni donnée de santé ni téléphone.
/// </summary>
public class MonthlyAttendanceSummaryDocumentTests
{
    private readonly MonthlyAttendanceSummaryPdfGenerator _generator = new();

    public MonthlyAttendanceSummaryDocumentTests() => PdfFonts.EnsureRegistered();

    private static readonly DateOnly[] ThreeDays = [new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7)];

    private static MonthlySummaryRowDto Row(int index, int present = 0, int absent = 0, int onLeave = 0, int notRecorded = 0, string? detail = null) =>
        new(Guid.NewGuid(), $"Élève n{index:000}", null, $"M-{index:000}", detail, present, absent, onLeave, notRecorded);

    private static MonthlyAttendanceSummaryReportDto Report(
        IReadOnlyList<MonthlySummaryRowDto> rows, IReadOnlyList<DateOnly>? days = null,
        AttendanceAudience audience = AttendanceAudience.AllBoarders, string group = "CM2") =>
        new("École A", "Médina, Dakar", null, "2026-2027",
            new MonthlySummaryDto(new DateOnly(2026, 10, 1), Guid.NewGuid(), "Déjeuner", audience, Guid.NewGuid(), group, days ?? ThreeDays, rows),
            new DateTimeOffset(2026, 10, 31, 18, 0, 0, TimeSpan.Zero));

    private string TextOf(MonthlyAttendanceSummaryReportDto report) => PdfText.Extract(_generator.Generate(report, null));

    [Fact]
    public void The_Summary_Prints_The_School_The_Session_The_Month_The_Group_And_Each_Student_With_Counts()
    {
        var text = TextOf(Report([Row(1, present: 2, absent: 1), Row(2, onLeave: 2, notRecorded: 1)]));

        text.Should().Contain("RÉCAPITULATIF MENSUEL DU POINTAGE").And.Contain("Déjeuner").And.Contain("Octobre 2026")
            .And.Contain("Classe : CM2").And.Contain("École A").And.Contain("Année scolaire 2026-2027");
        text.Should().Contain("Élève n001").And.Contain("M-001").And.Contain("Élève n002");
        text.Should().Contain("Présents").And.Contain("Absents").And.Contain("En perm.").And.Contain("Non pointés");
    }

    [Fact]
    public void The_Pointed_Days_Are_Listed_So_The_Totals_Can_Be_Checked()
    {
        var text = TextOf(Report([Row(1, present: 3)]));

        text.Should().Contain("Jours pointés : 3").And.Contain("(5, 6, 7)");
    }

    [Fact]
    public void The_Group_Total_Line_Adds_Up_Each_Column()
    {
        var text = TextOf(Report([Row(1, present: 2, absent: 1), Row(2, present: 1, onLeave: 2)]));

        text.Should().Contain("Total du groupe");
        // 3 présents, 1 absent, 2 en permission, 0 non pointé : les totaux suivent le libellé (l'extraction ne garde pas les espaces).
        text.Should().Contain("Total du groupe3120");
    }

    [Fact]
    public void A_Month_Without_Any_Roll_Says_So_Instead_Of_Printing_Misleading_Zeros_Silently()
    {
        var text = TextOf(Report([Row(1, notRecorded: 0)], days: []));

        text.Should().Contain("Jours pointés : 0").And.Contain("Aucun pointage ce mois-ci");
        text.Should().Contain("Élève n001", "la liste du groupe reste imprimée");
    }

    [Fact]
    public void A_Group_Without_Students_Prints_A_Plain_Sentence()
    {
        TextOf(Report([])).Should().Contain("Aucun élève dans ce groupe.");
    }

    [Theory]
    [InlineData(AttendanceAudience.Interns, "Pavillon : Pavillon A")]
    [InlineData(AttendanceAudience.Halqa, "Halqa de l'Oustaz : Pavillon A")]
    [InlineData(AttendanceAudience.HalfBoarders, "Classe : Pavillon A")]
    public void The_Group_Is_Labelled_According_To_The_Audience(AttendanceAudience audience, string expected)
    {
        TextOf(Report([Row(1, present: 1)], audience: audience, group: "Pavillon A")).Should().Contain(expected);
    }

    [Fact]
    public void The_Detail_Column_Appears_Only_When_A_Student_Has_A_Detail()
    {
        var with = TextOf(Report([Row(1, present: 1, detail: "Interne"), Row(2, present: 1, detail: "Demi-pensionnaire")]));
        var without = TextOf(Report([Row(1, present: 1)]));

        with.Should().Contain("Détail").And.Contain("Demi-pensionnaire");
        without.Should().NotContain("Détail");
    }

    [Fact]
    public void A_Long_List_Spans_Pages_And_Repeats_The_Table_Header_On_Each()
    {
        var rows = Enumerable.Range(0, 120).Select(i => Row(i, present: 1)).ToArray();

        var pdf = _generator.Generate(Report(rows), null);

        using var document = PdfDocument.Open(pdf);
        document.NumberOfPages.Should().BeGreaterThan(1);
        document.GetPages().Should().OnlyContain(p => p.Text.Contains("Présents"), "l'en-tête du tableau se répète sur chaque page");
    }

    [Fact]
    public void The_Report_Shape_Carries_No_Health_Or_Phone_Data()
    {
        string[] forbidden = ["medical", "phone", "contact", "allowedexit", "emergency", "health", "sante"];
        var names = typeof(MonthlyAttendanceSummaryReportDto).GetProperties().Concat(typeof(MonthlySummaryDto).GetProperties())
            .Concat(typeof(MonthlySummaryRowDto).GetProperties()).Select(p => p.Name);

        names.Should().NotContain(name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }
}
