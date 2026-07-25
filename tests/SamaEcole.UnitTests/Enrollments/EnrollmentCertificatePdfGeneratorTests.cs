using System.Text;
using FluentAssertions;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Le moteur PDF (QuestPDF) du Certificat de Scolarité (A4, normes M.E.N. Sénégal) produit-il un
/// document valide, sans lever d'exception, y compris aux cas limites (IA/IEF/ville absents, préfixe
/// de cycle sans nom d'établissement saisi, logo présent ou illisible) ? La fidélité au formalisme
/// administratif se vérifie à l'œil ; ces tests couvrent la NON-RÉGRESSION de la génération.
/// </summary>
public class EnrollmentCertificatePdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // PNG 1×1 transparent, valide — exerce l'incrustation réelle du logo sans dépendre du réseau.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static EnrollmentCertificateDto Certificate(
        string? city = "Dakar",
        string? inspectionAcademie = "Dakar",
        string? inspectionEducationFormation = "Dakar 1",
        string? headingName = "Popenguine") => new(
        EnrollmentId: Guid.NewGuid(),
        CertificateNumber: "CS-1A2B3C4D",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        StudentBirthDate: new DateOnly(2015, 3, 12),
        StudentBirthPlace: "Dakar",
        StudentGender: "Female",
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        EnrolledAt: new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
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
        var pdf = new EnrollmentCertificatePdfGenerator().Generate(Certificate(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "un certificat complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_IA_IEF_And_City()
    {
        var pdf = new EnrollmentCertificatePdfGenerator().Generate(
            Certificate(city: null, inspectionAcademie: null, inspectionEducationFormation: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Missing_Heading_Name()
    {
        // École qui n'a pas encore renseigné son nom d'établissement (Paramètres) : la ligne
        // s'imprime réduite à son préfixe de cycle, jamais un repli sur SchoolName.
        var pdf = new EnrollmentCertificatePdfGenerator().Generate(Certificate(headingName: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new EnrollmentCertificatePdfGenerator().Generate(Certificate(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Certificate_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet de sécurité : des octets pathologiques (ni PNG ni JPEG décodable) ne doivent JAMAIS
        // empêcher l'émission du certificat officiel — le générateur régénère alors sans le logo.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new EnrollmentCertificatePdfGenerator().Generate(Certificate(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new EnrollmentCertificatePdfGenerator().Generate(Certificate(), logo: unreadable));
    }
}
