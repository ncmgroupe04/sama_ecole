using FluentAssertions;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Infrastructure.Documents;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Ticket JGK-G03 — critère explicite du ticket : « un cas à 12 matières ne dépasse pas une page A5 ».
/// GenerateImages rend une image par page ; compter les images obtenues est le moyen le plus direct de
/// vérifier l'absence de débordement sans dépendre d'un outil de comparaison visuelle externe.
/// </summary>
public class ReportCardDocumentTests
{
    static ReportCardDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static ReportCardDto BuildReportCard(int subjectCount)
    {
        var subjects = Enumerable.Range(1, subjectCount)
            .Select(i => new SubjectGradeDto(
                Guid.NewGuid(), $"Matière {i}", Devoir: 12 + i % 5, Composition: 10 + i % 8,
                Average: 11 + i % 6, Coefficient: 1 + i % 4, WeightedPoints: (11 + i % 6) * (1 + i % 4)))
            .ToList();

        var ranks = subjects.ToDictionary(s => s.SubjectId, s => 1);

        return new ReportCardDto(
            SchoolName: "École de test",
            SchoolLogoUrl: null,
            StudentFullName: "Élève de Test avec un Nom Assez Long",
            BirthDate: new DateOnly(2012, 3, 14),
            ClassroomName: "3e A",
            Matricule: "ELEV-2026-0001",
            ClassSize: 42,
            SchoolYearLabel: "2026-2027",
            TermLabel: "1er trimestre",
            GradingScale: 20,
            Subjects: subjects,
            TotalCoefficients: subjects.Sum(s => s.Coefficient),
            TotalPoints: subjects.Sum(s => s.WeightedPoints),
            GeneralAverage: 13.27m,
            GeneralRank: 3,
            SubjectRanks: ranks,
            Mention: "Bien",
            TermRecaps:
            [
                new ReportCardTermRecap("1er trimestre", 1, 13.27m),
                new ReportCardTermRecap("2e trimestre", 2, null),
                new ReportCardTermRecap("3e trimestre", 3, null)
            ],
            AnnualAverage: 13.27m,
            AnnualRank: 3);
    }

    [Fact]
    public void A_Twelve_Subject_Report_Card_Fits_On_A_Single_A5_Page()
    {
        var document = new ReportCardDocument(BuildReportCard(12), logo: null);

        var pages = document.GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "le ticket JGK-G03 exige qu'un bulletin à 12 matières ne déborde jamais sur une seconde page A5");
    }

    [Fact]
    public void The_Generated_Document_Is_A_Valid_Non_Empty_Pdf()
    {
        var bytes = new ReportCardDocument(BuildReportCard(6), logo: null).GeneratePdf();

        bytes.Should().NotBeEmpty();
        // Signature standard d'un fichier PDF : les 5 premiers octets valent "%PDF-".
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
