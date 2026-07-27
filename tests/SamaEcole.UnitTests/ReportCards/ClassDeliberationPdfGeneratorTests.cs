using FluentAssertions;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// <see cref="ClassDeliberationPdfGenerator"/> — même garde que ReportCardPdfGenerator/ReceiptPdfGenerator :
/// un logo illisible ne doit jamais empêcher l'émission du PV, seulement le régénérer sans lui.
/// </summary>
public class ClassDeliberationPdfGeneratorTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static IReadOnlyList<ReportCardDto> OneStudent() =>
    [
        new ReportCardDto(
            SchoolName: "École de test", SchoolLogoUrl: null,
            InspectionAcademie: "Thies", InspectionEducationFormation: "Mbour 1",
            HeadingPrefix: "ÉCOLE ÉLÉMENTAIRE DE", HeadingName: "Popenguine",
            StudentFullName: "Awa Sow", BirthDate: new DateOnly(2015, 3, 14), BirthPlace: "Dakar",
            ClassroomName: "CM2", Cycle: CycleType.Primaire, Matricule: "ELEV-2026-0001", ClassSize: 1,
            IsRepeating: false, SchoolYearLabel: "2026-2027", TermLabel: "1er trimestre", GradingScale: 10,
            Subjects: [], TotalCoefficients: 4m, TotalPoints: 36m, GeneralAverage: 9m, GeneralRank: 1,
            SubjectRanks: new Dictionary<Guid, int>(), SubjectAppreciations: new Dictionary<Guid, string?>(),
            Mention: "Bien", Absences: null, Retards: null, TotalAbsences: null, TermRecaps: [],
            AnnualAverage: null, AnnualRank: null, DisciplinaryMention: null,
            CouncilDecision: CouncilDecision.Admitted, CouncilObservations: null)
    ];

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new ClassDeliberationPdfGenerator().Generate(OneStudent(), TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Pv_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ClassDeliberationPdfGenerator().Generate(OneStudent(), unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new ClassDeliberationPdfGenerator().Generate(OneStudent(), unreadable));
    }
}
