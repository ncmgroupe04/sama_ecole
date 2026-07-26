using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Non-régression du moteur PDF (QuestPDF) de l'attestation de travail.</summary>
public class WorkCertificatePdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static WorkCertificateDto Certificate(
        string? city = "Dakar",
        string contractType = "Permanent") => new(
        ContractId: Guid.NewGuid(),
        CertificateNumber: "AT-1A2B3C4D",
        EmployeeFullName: "Fatou Ndiaye",
        EmployeeRole: "Enseignant",
        ContractType: contractType,
        SinceDate: new DateTimeOffset(2023, 9, 1, 8, 0, 0, TimeSpan.Zero),
        IssuedAt: new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolCity: city,
        SchoolPhone: "+221338001122",
        SchoolNinea: "123456789",
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new WorkCertificatePdfGenerator().Generate(Certificate(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une attestation complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_City()
    {
        var pdf = new WorkCertificatePdfGenerator().Generate(Certificate(city: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Handles_Vacataire_Contract_Type()
    {
        var pdf = new WorkCertificatePdfGenerator().Generate(Certificate(contractType: "Vacataire"), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Certificate_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new WorkCertificatePdfGenerator().Generate(Certificate(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
    }
}
