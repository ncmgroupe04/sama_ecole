using FluentAssertions;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// <see cref="ReportCardPdfGenerator"/> — le bulletin est un document officiel, il doit TOUJOURS
/// s'émettre : un logo, une signature ou un cachet illisible (octets corrompus, ni PNG ni JPEG
/// décodable) ne doit jamais empêcher l'impression, seulement faire retomber le document sur ses
/// emplacements vides d'origine (même garde que ReceiptPdfGenerator/PayslipPdfGenerator).
/// </summary>
public class ReportCardPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static ReportCardDto MinimalReportCard() => new(
        SchoolName: "École de test",
        SchoolLogoUrl: null,
        InspectionAcademie: "Thies",
        InspectionEducationFormation: "Mbour 1",
        HeadingPrefix: "LYCÉE DE",
        HeadingName: "Popenguine",
        StudentFullName: "Awa Fall",
        BirthDate: new DateOnly(2012, 3, 14),
        BirthPlace: "Dakar",
        ClassroomName: "3e A",
        Cycle: CycleType.Lycee,
        Matricule: "ELEV-2026-0001",
        ClassSize: 30,
        IsRepeating: false,
        SchoolYearLabel: "2026-2027",
        TermLabel: "1er trimestre",
        GradingScale: 20,
        Subjects: [],
        TotalCoefficients: 0m,
        TotalPoints: 0m,
        GeneralAverage: 0m,
        GeneralRank: 1,
        SubjectRanks: new Dictionary<Guid, int>(),
        SubjectAppreciations: new Dictionary<Guid, string?>(),
        Mention: null,
        Absences: null,
        Retards: null,
        TotalAbsences: null,
        TermRecaps: [],
        AnnualAverage: null,
        AnnualRank: null,
        DisciplinaryMention: null,
        CouncilDecision: null,
        CouncilObservations: null);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        System.Text.Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-");
    }

    [Fact]
    public void Generate_Embeds_Logo_Signature_And_Stamp_Without_Error()
    {
        var pdf = new ReportCardPdfGenerator().Generate(MinimalReportCard(), logo: TinyPng, directorSignature: TinyPng, officialStamp: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Plain_Report_Card_When_The_Signature_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ReportCardPdfGenerator().Generate(MinimalReportCard(), logo: null, directorSignature: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new ReportCardPdfGenerator().Generate(MinimalReportCard(), logo: null, directorSignature: unreadable));
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Plain_Report_Card_When_The_Stamp_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ReportCardPdfGenerator().Generate(MinimalReportCard(), logo: null, officialStamp: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new ReportCardPdfGenerator().Generate(MinimalReportCard(), logo: null, officialStamp: unreadable));
    }

    [Fact]
    public void Generate_Produces_A_Valid_Pdf_Without_Any_Images()
    {
        var pdf = new ReportCardPdfGenerator().Generate(MinimalReportCard(), logo: null);

        ShouldBeAValidPdf(pdf);
    }
}
