using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Évolution N°7 — tableau récapitulatif du PV du conseil de classe : ventilation Filles/Garçons, présents,
/// classés, taux de réussite (≥ moitié du barème), distinctions et décisions ; rendu PDF des deux portées.
/// </summary>
public class DeliberationStatisticsTests
{
    static DeliberationStatisticsTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static ReportCardDto Card(
        string gender, decimal? average, bool sat = true, decimal? annual = null,
        DisciplinaryMention? distinction = null, CouncilDecision? decision = null, CouncilDecision? proposed = null) => new(
        SchoolName: "Lycée de test", SchoolLogoUrl: null, InspectionAcademie: "Dakar", InspectionEducationFormation: "Dakar Plateau",
        HeadingPrefix: "LYCÉE", HeadingName: "Test", StudentFullName: $"Élève {Guid.NewGuid():N}", BirthDate: new DateOnly(2008, 1, 1),
        BirthPlace: "Dakar", ClassroomName: "Terminale S2", Cycle: CycleType.Lycee, Matricule: "M-1", ClassSize: 4,
        IsRepeating: false, SchoolYearLabel: "2026-2027", TermLabel: "1er semestre", GradingScale: 20, Subjects: [],
        TotalCoefficients: average is null ? 0 : 10, TotalPoints: (average ?? 0) * 10, GeneralAverage: average ?? 0, GeneralRank: 1,
        SubjectRanks: new Dictionary<Guid, int>(), SubjectAppreciations: new Dictionary<Guid, string?>(), Mention: null,
        Absences: null, Retards: null, TotalAbsences: null,
        TermRecaps: annual is null ? [] : [new ReportCardTermRecap("1er semestre", 1, annual)],
        AnnualAverage: annual, AnnualRank: annual is null ? null : 1,
        DisciplinaryMention: distinction, CouncilDecision: decision, CouncilObservations: null,
        StudentGender: gender, ProposedCouncilDecision: proposed, SatComposition: sat);

    [Fact]
    public void The_Period_Summary_Is_Broken_Down_By_Gender()
    {
        var stats = DeliberationStatistics.Compute(
        [
            Card("F", 14, distinction: DisciplinaryMention.Felicitations),
            Card("F", 9),
            Card("M", 11, distinction: DisciplinaryMention.Encouragements),
            Card("M", null, sat: false)
        ], DeliberationScope.Period);

        stats.Girls.Should().Be(new GenderBreakdown(Enrolled: 2, Present: 2, Ranked: 2, Passed: 1));
        stats.Boys.Should().Be(new GenderBreakdown(Enrolled: 2, Present: 1, Ranked: 1, Passed: 1));
        stats.Total.Should().Be(new GenderBreakdown(4, 3, 3, 2));
        stats.Total.PassRate.Should().Be(66.67m);
        stats.Girls.PassRate.Should().Be(50m);
        stats.ClassAverage.Should().Be(11.33m);
        stats.Highest.Should().Be(14);
        stats.Lowest.Should().Be(9);
        stats.Felicitations.Should().Be(1);
        stats.Encouragements.Should().Be(1);
    }

    [Fact]
    public void Nobody_Ranked_Gives_No_Rate_Rather_Than_Zero_Percent()
        => DeliberationStatistics.Compute([Card("F", null, sat: false)], DeliberationScope.Period)
            .Total.PassRate.Should().BeNull();

    [Fact]
    public void The_Annual_Summary_Counts_Decisions_Taken_Or_Else_Proposed()
    {
        var stats = DeliberationStatistics.Compute(
        [
            Card("F", 12, annual: 12.5m, decision: CouncilDecision.Admitted),
            Card("M", 9, annual: 9m, proposed: CouncilDecision.AllowedToRepeat),
            Card("M", 7, annual: 7m, decision: CouncilDecision.AllowedToRepeat, proposed: CouncilDecision.Excluded),
            Card("F", null, sat: false)
        ], DeliberationScope.Annual);

        stats.Total.Should().Be(new GenderBreakdown(4, 3, 3, 1));
        stats.Admitted.Should().Be(1);
        stats.AllowedToRepeat.Should().Be(2, "la décision du conseil l'emporte sur la proposition");
        stats.Excluded.Should().Be(0);
    }

    [Theory]
    [InlineData(DeliberationScope.Period)]
    [InlineData(DeliberationScope.Annual)]
    public void Both_Minutes_Render_A_Valid_Pdf(DeliberationScope scope)
    {
        var pdf = new ClassDeliberationDocument(
        [
            Card("F", 15, annual: 14m, distinction: DisciplinaryMention.Felicitations, proposed: CouncilDecision.Admitted),
            Card("M", 8, annual: 8m, proposed: CouncilDecision.Excluded),
            Card("M", null, sat: false)
        ], logo: null, scope).GeneratePdf();

        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
