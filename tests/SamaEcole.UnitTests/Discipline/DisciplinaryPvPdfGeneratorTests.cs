using System.Text;
using FluentAssertions;
using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Discipline;

/// <summary>Non-régression du moteur PDF (QuestPDF) du PV de sanction disciplinaire.</summary>
public class DisciplinaryPvPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static DisciplinaryPvDto Pv(
        string? guardianName = "Moussa Fall",
        string? guardianPhone = "+221771234567",
        string sanctionType = "Exclusion temporaire") => new(
        DisciplineRecordId: Guid.NewGuid(),
        PvNumber: "PV-1A2B3C4D",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        StudentBirthDate: new DateOnly(2015, 3, 12),
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        Date: new DateTime(2026, 3, 15),
        SanctionType: sanctionType,
        Reason: "Bagarre dans la cour de récréation",
        GuardianName: guardianName,
        GuardianPhone: guardianPhone,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: "Rue 12, Médina, Dakar",
        SchoolCity: "Dakar",
        InspectionAcademie: "Dakar",
        InspectionEducationFormation: "Dakar 1",
        HeadingPrefix: "ÉCOLE ÉLÉMENTAIRE DE",
        HeadingName: "Popenguine",
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        SurveillantSignatureUrl: null);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new DisciplinaryPvPdfGenerator().Generate(Pv(), logo: null, qrCodeImage: TinyPng, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "un PV complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Guardian_Contact_Is_Registered()
    {
        var pdf = new DisciplinaryPvPdfGenerator().Generate(
            Pv(guardianName: null, guardianPhone: null), logo: null, qrCodeImage: TinyPng, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
    }

    [Theory]
    [InlineData("Avertissement")]
    [InlineData("Blâme")]
    [InlineData("Retenue")]
    [InlineData("Exclusion temporaire")]
    public void Generate_Handles_Every_Sanction_Type(string sanctionType)
    {
        var pdf = new DisciplinaryPvPdfGenerator().Generate(Pv(sanctionType: sanctionType), logo: null, qrCodeImage: TinyPng, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Surveillant_Signature_Without_Error()
    {
        var pdf = new DisciplinaryPvPdfGenerator().Generate(Pv(), logo: null, qrCodeImage: TinyPng, surveillantSignature: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Pv_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new DisciplinaryPvPdfGenerator().Generate(Pv(), logo: unreadable, qrCodeImage: TinyPng, surveillantSignature: null);

        act.Should().NotThrow();
    }
}
