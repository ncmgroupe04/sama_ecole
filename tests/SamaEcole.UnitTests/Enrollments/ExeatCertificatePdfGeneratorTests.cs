using System.Text;
using FluentAssertions;
using SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Non-régression du moteur PDF (QuestPDF) du Certificat d'Exéat, sur le même principe que
/// <see cref="EnrollmentCertificatePdfGeneratorTests"/> : produit-il un PDF valide, y compris aux cas
/// limites (IA/IEF/ville absents, solde nul ou restant dû, logo présent/illisible) ?
/// </summary>
public class ExeatCertificatePdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // PNG 1×1 transparent, valide — sert à la fois de logo et de QR code factice dans ces tests.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static ExeatCertificateDto Certificate(
        string? city = "Dakar",
        string? inspectionAcademie = "Dakar",
        string? inspectionEducationFormation = "Dakar 1",
        string? headingName = "Popenguine",
        decimal totalDue = 150_000,
        decimal amountPaid = 150_000,
        string motive = "Transfert vers un autre établissement") => new(
        EnrollmentId: Guid.NewGuid(),
        CertificateNumber: "EX-1A2B3C4D",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        StudentBirthDate: new DateOnly(2015, 3, 12),
        StudentBirthPlace: "Dakar",
        StudentGender: "Female",
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        Motive: motive,
        LeftAt: new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero),
        TotalDue: totalDue,
        AmountPaid: amountPaid,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolCity: city,
        InspectionAcademie: inspectionAcademie,
        InspectionEducationFormation: inspectionEducationFormation,
        HeadingPrefix: "ÉCOLE ÉLÉMENTAIRE DE",
        HeadingName: headingName,
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new ExeatCertificatePdfGenerator().Generate(Certificate(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "un exéat complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_IA_IEF_And_City()
    {
        var pdf = new ExeatCertificatePdfGenerator().Generate(
            Certificate(city: null, inspectionAcademie: null, inspectionEducationFormation: null),
            logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Mentions_Outstanding_Balance_When_Not_Fully_Paid()
    {
        var pdf = new ExeatCertificatePdfGenerator().Generate(
            Certificate(totalDue: 150_000, amountPaid: 50_000), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new ExeatCertificatePdfGenerator().Generate(Certificate(), logo: TinyPng, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Certificate_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ExeatCertificatePdfGenerator().Generate(Certificate(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new ExeatCertificatePdfGenerator().Generate(Certificate(), logo: unreadable, qrCodeImage: TinyPng));
    }
}
