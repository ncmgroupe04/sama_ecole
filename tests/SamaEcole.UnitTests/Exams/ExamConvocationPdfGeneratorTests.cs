using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Exams;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

/// <summary>
/// Le moteur PDF (QuestPDF) de la convocation d'examen produit-il un document valide, sans exception,
/// y compris aux cas limites (série/ville absentes, logo présent ou illisible) ? Surtout : la
/// convocation est passée en <b>A5 paysage</b> pour ne plus gâcher une A4 sur une pièce remise en main
/// propre — ces tests verrouillent le fait qu'elle tient sur UNE seule page, même chargée (noms longs,
/// centre au libellé interminable). La fidélité visuelle se vérifie à l'œil.
/// </summary>
public class ExamConvocationPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // PNG 1×1 transparent, valide — exerce l'incrustation réelle du logo / QR sans dépendre du réseau.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static ExamConvocationModel Convocation(
        string? series = "S2",
        string? city = "Thiès",
        string studentFullName = "Cheikh Diop",
        string examCenterName = "Lycée Malick Sy",
        string? inspectionAcademie = "Thiès",
        string? inspectionEducationFormation = "Thiès Ville") => new(
        Reference: "CONV-2027-000123",
        SchoolName: "Collège d'Enseignement Moyen Les Filaos",
        InspectionAcademie: inspectionAcademie,
        InspectionEducationFormation: inspectionEducationFormation,
        SchoolCity: city,
        IssuedAt: new DateTimeOffset(2027, 6, 1, 9, 0, 0, TimeSpan.Zero),
        StudentFullName: studentFullName,
        StudentMatricule: "ELEV-2025-0042",
        ClassroomName: "3e S2",
        ExamType: "BFEM",
        Series: series,
        CandidateNumber: "2027-BFEM-04128",
        ExamCenterName: examCenterName);

    private static ExamConvocationPdfGenerator Generator() =>
        new(Mock.Of<ILogger<ExamConvocationPdfGenerator>>());

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = Generator().Generate(Convocation(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "une convocation complète n'est pas un fichier vide");
    }

    /// <summary>
    /// Le nombre de pages se lit directement dans l'objet racine <c>/Type /Pages /Count N</c> du PDF
    /// (structure stable des documents à page unique QuestPDF/SkiaSharp) — même technique que
    /// <c>ReceiptPdfGeneratorTests</c>, sans dépendre d'une bibliothèque d'extraction PDF.
    /// </summary>
    [Fact]
    public void Generate_Stays_On_A_Single_A5_Page()
    {
        var pdf = Generator().Generate(Convocation(), logo: TinyPng, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1",
            "la convocation A5 paysage ne doit jamais déborder sur une deuxième page");
    }

    [Fact]
    public void Generate_Stays_On_A_Single_Page_Even_With_Long_Names_And_A_Verbose_Center()
    {
        var pdf = Generator().Generate(
            Convocation(
                studentFullName: "Mouhamadou Moustapha Cheikh Ahmadou Bamba Diop Fall Ndiaye",
                examCenterName: "Centre d'examen du Lycée d'Enseignement Général et Technique El Hadji Malick Sy de Thiès",
                inspectionAcademie: "Inspection d'Académie de Thiès Département",
                inspectionEducationFormation: "Inspection de l'Éducation et de la Formation de Thiès Ville Commune"),
            logo: TinyPng,
            qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1",
            "les paddings sont calibrés pour absorber un contenu chargé sans passer à deux pages");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Series_And_City()
    {
        // Le CFEE n'a pas de série ; une école peut n'avoir pas saisi sa ville. La mention absente ne
        // s'imprime pas, elle ne fait pas tomber le document.
        var pdf = Generator().Generate(
            Convocation(series: null, city: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1");
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = Generator().Generate(Convocation(), logo: TinyPng, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Convocation_When_Logo_Bytes_Are_Unreadable()
    {
        // Des octets pathologiques (ni PNG ni JPEG décodable) ne doivent JAMAIS empêcher l'émission de
        // la convocation — PdfRenderGuard régénère alors sans le logo.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => Generator().Generate(Convocation(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
        ShouldBeAValidPdf(Generator().Generate(Convocation(), logo: unreadable, qrCodeImage: TinyPng));
    }
}
