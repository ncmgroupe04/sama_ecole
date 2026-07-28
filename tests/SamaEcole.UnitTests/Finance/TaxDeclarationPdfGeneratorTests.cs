using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Le moteur PDF (QuestPDF) de la Déclaration Fiscale produit-il un document valide, sans lever
/// d'exception, y compris aux cas limites (NINEA absent, logo présent ou illisible) ?
/// </summary>
public class TaxDeclarationPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static TaxDeclarationDto Declaration(string? ninea = "SN-DKR-2020-B-1234", string? address = "Rue 12, Médina, Dakar") => new(
        Id: Guid.NewGuid(),
        DeclarationNumber: "DECL-1A2B3C4D-202603",
        Month: 3,
        Year: 2026,
        TotalIpres: 25_200m,
        TotalCss: 4_410m,
        TotalVrs: 9_000m,
        TotalBrs: 14_160m,
        TvaCollected: 180_000m,
        TvaDeductible: 60_000m,
        NetTva: 120_000m,
        TotalDueToState: 143_160m,
        CreatedAt: DateTimeOffset.UtcNow,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: address,
        SchoolCity: "Dakar",
        SchoolNinea: ninea,
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une déclaration complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Ninea_And_Address()
    {
        var pdf = new TaxDeclarationPdfGenerator().Generate(Declaration(ninea: null, address: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Declaration_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: unreadable));
    }
}
