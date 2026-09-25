using FluentAssertions;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Bulletin d'un élève dispensé d'une matière OBLIGATOIRE (spec §4.4) : la ligne reste, à sa place, marquée
/// « Dispensé(e) » ; le bulletin tient toujours sur une page A5, dans les trois variantes de tableau
/// (secondaire, primaire, grille APC). Les totaux, eux, sont ceux du sommaire : ils sont vérifiés côté
/// données (ExemptSubjectReportCardDataTests).
/// </summary>
public class ExemptSubjectReportCardTests
{
    static ExemptSubjectReportCardTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly Guid Eps = Guid.NewGuid();

    private static ReportCardDto BuildReportCard(int subjectCount)
    {
        var subjects = Enumerable.Range(1, subjectCount)
            .Select(i => new SubjectGradeDto(
                Guid.NewGuid(), $"Matière {i}", Devoir1: 12 + i % 5, Devoir2: null, Composition: 10 + i % 8,
                DevoirAverage: 12 + i % 5, Average: 11 + i % 6, Coefficient: 1 + i % 4, WeightedPoints: (11 + i % 6) * (1 + i % 4)))
            .ToList();

        var ranks = subjects.ToDictionary(s => s.SubjectId, s => 1);
        var appreciations = subjects.ToDictionary(s => s.SubjectId, s => (string?)"Bien");

        return new ReportCardDto(
            SchoolName: "École de test",
            SchoolLogoUrl: null,
            InspectionAcademie: "Thies",
            InspectionEducationFormation: "Mbour 1",
            HeadingPrefix: "LYCÉE DE",
            HeadingName: "Popenguine",
            StudentFullName: "Élève de Test avec un Nom Assez Long",
            BirthDate: new DateOnly(2012, 3, 14),
            BirthPlace: "Saint-Louis",
            ClassroomName: "3e A",
            Cycle: CycleType.Lycee,
            Matricule: "ELEV-2026-0001",
            ClassSize: 42,
            IsRepeating: false,
            SchoolYearLabel: "2026-2027",
            TermLabel: "1er trimestre",
            GradingScale: 20,
            Subjects: subjects,
            TotalCoefficients: subjects.Sum(s => s.Coefficient),
            TotalPoints: subjects.Sum(s => s.WeightedPoints),
            GeneralAverage: 13.27m,
            GeneralRank: 3,
            SubjectRanks: ranks,
            SubjectAppreciations: appreciations,
            Mention: "Bien",
            Absences: 1,
            Retards: 0,
            TotalAbsences: 2,
            TermRecaps:
            [
                new ReportCardTermRecap("1er trimestre", 1, 13.27m),
                new ReportCardTermRecap("2e trimestre", 2, null),
                new ReportCardTermRecap("3e trimestre", 3, null)
            ],
            AnnualAverage: 13.27m,
            AnnualRank: 3,
            DisciplinaryMention: null,
            CouncilDecision: null,
            CouncilObservations: null);
    }

    private static int PageCount(ReportCardDto card) =>
        new ReportCardDocument(card, logo: null).GenerateImages(ImageGenerationSettings.Default).Count();

    [Fact]
    public void Without_Exemption_The_Rows_Are_The_Graded_Subjects_In_Their_Order()
    {
        var card = BuildReportCard(5);

        var rows = new ReportCardDocument(card, logo: null).GradeRows();

        rows.Select(r => r.SubjectId).Should().Equal(card.Subjects.Select(s => s.SubjectId));
        rows.Should().OnlyContain(r => r.Graded != null && r.Exempt == null);
    }

    [Fact]
    public void An_Exempted_Subject_Keeps_The_Alphabetical_Rank_It_Would_Have_Had_If_It_Were_Graded()
    {
        // « Matière 3 - option » se place entre « Matière 3 » et « Matière 4 » : quatrième ligne du tableau.
        var card = BuildReportCard(5) with { ExemptSubjects = [new ExemptSubjectDto(Eps, "Matière 3 - option", 1m)] };

        var rows = new ReportCardDocument(card, logo: null).GradeRows();

        rows.Should().HaveCount(6);
        rows[3].SubjectId.Should().Be(Eps);
        rows[3].Graded.Should().BeNull();
        rows[3].Exempt.Should().NotBeNull();
        rows.Where(r => r.SubjectId != Eps).Should().OnlyContain(r => r.Graded != null);
    }

    [Fact]
    public void An_Exempted_Subject_That_Sorts_First_Is_Printed_First()
    {
        var card = BuildReportCard(5) with { ExemptSubjects = [new ExemptSubjectDto(Eps, "EPS", 1m)] };

        new ReportCardDocument(card, logo: null).GradeRows()[0].SubjectId.Should().Be(Eps);
    }

    [Fact]
    public void The_Exempt_Label_Is_The_Validated_One()
        => ReportCardDocument.ExemptLabel.Should().Be("Dispensé(e)");

    [Theory]
    [InlineData(CycleType.College)]
    [InlineData(CycleType.Lycee)]
    public void A_Secondary_Report_Card_With_An_Exempted_Subject_Fits_On_One_A5_Page(CycleType cycle)
    {
        var card = BuildReportCard(11) with
        {
            Cycle = cycle,
            ExemptSubjects = [new ExemptSubjectDto(Eps, "EPS", 1m)]
        };

        PageCount(card).Should().Be(1, "douze lignes au total : la limite que le ticket JGK-G03 impose à tout bulletin");
        new ReportCardDocument(card, logo: null).GeneratePdf().Should().NotBeEmpty();
    }

    [Fact]
    public void A_Primary_Report_Card_With_An_Exempted_Subject_Fits_On_One_A5_Page()
    {
        var card = BuildReportCard(7) with
        {
            Cycle = CycleType.Primaire,
            GradingScale = 10,
            ExemptSubjects = [new ExemptSubjectDto(Eps, "EPS", 1m)]
        };

        PageCount(card).Should().Be(1);
        new ReportCardDocument(card, logo: null).GeneratePdf().Should().NotBeEmpty();
    }

    [Fact]
    public void An_Evaluation_Grid_With_An_Exempted_Line_Fits_On_One_A5_Page()
    {
        var structure = new EvaluationStructureDto("Activités", "Contrôles",
        [
            new EvaluationGroupDto(Guid.NewGuid(), "Français",
            [
                new EvaluationLineDto(Guid.NewGuid(), "Ressources", 32m, 40m, "Bien"),
                new EvaluationLineDto(Guid.NewGuid(), "Compétences", 48m, 60m, "Bien")
            ]),
            // Matière simple dispensée : une seule ligne, sans libellé de seconde colonne.
            new EvaluationGroupDto(Eps, "EPS", [new EvaluationLineDto(Eps, null, null, 20m, null, IsExempt: true)])
        ]);
        var card = BuildReportCard(0) with
        {
            Cycle = CycleType.Primaire,
            GradingScale = 10,
            EvaluationStructure = structure
        };

        PageCount(card).Should().Be(1);
        new ReportCardDocument(card, logo: null).GeneratePdf().Should().NotBeEmpty();
    }
}
