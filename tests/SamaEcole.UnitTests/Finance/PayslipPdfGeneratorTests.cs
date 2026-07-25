using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Le moteur PDF (QuestPDF) du Bulletin de Paie produit-il un document valide, sans lever d'exception,
/// y compris aux cas limites (contrat Vacataire sans heures, NINEA absent, logo présent ou illisible) ?
/// </summary>
public class PayslipPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static PayslipDto Payslip(
        string contractType = "Permanent",
        decimal hoursWorked = 0m,
        string? ninea = "SN-DKR-2020-B-1234",
        string? city = "Dakar") => new(
        FichePaieId: Guid.NewGuid(),
        PayslipNumber: "BP-1A2B3C4D",
        EmployeeFullName: "Fatou Ndiaye",
        EmployeeRole: "Enseignant",
        ContractType: contractType,
        Month: 3,
        Year: 2026,
        HoursWorked: hoursWorked,
        GrossSalary: 300_000m,
        TransportAllowance: 15_000m,
        IpresEmployee: 16_800m,
        IpresEmployer: 25_200m,
        CssEmployer: 4_410m,
        Vrs: 9_000m,
        Brs: 14_160m,
        NetSalary: 284_040m,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolCity: city,
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
        var pdf = new PayslipPdfGenerator().Generate(Payslip(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "un bulletin complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Vacataire_Contract_Without_Hours()
    {
        var pdf = new PayslipPdfGenerator().Generate(Payslip(contractType: "Vacataire", hoursWorked: 0m), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Ninea_And_City()
    {
        var pdf = new PayslipPdfGenerator().Generate(Payslip(ninea: null, city: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new PayslipPdfGenerator().Generate(Payslip(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Payslip_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new PayslipPdfGenerator().Generate(Payslip(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new PayslipPdfGenerator().Generate(Payslip(), logo: unreadable));
    }
}
