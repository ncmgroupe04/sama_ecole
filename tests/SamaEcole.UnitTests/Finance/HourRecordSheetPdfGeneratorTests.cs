using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Non-régression du moteur PDF (QuestPDF) de la fiche de suivi des heures (Vacataire).</summary>
public class HourRecordSheetPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static HourRecordSheetDto Sheet(IReadOnlyList<HourRecordLineDto>? lines = null)
    {
        var resolvedLines = lines ?? new List<HourRecordLineDto>
        {
            new(new DateOnly(2026, 3, 2), 3, "Mathématiques 6e"),
            new(new DateOnly(2026, 3, 9), 4, null)
        };

        return new HourRecordSheetDto(
            EmployeeContractId: Guid.NewGuid(),
            SheetNumber: "FH-1A2B3C4D-202603",
            EmployeeFullName: "Fatou Ndiaye",
            Month: 3,
            Year: 2026,
            HourlyRate: 5_000,
            Lines: resolvedLines,
            TotalHours: resolvedLines.Sum(l => l.Hours),
            SchoolName: "École Primaire Les Baobabs",
            SchoolAddress: "Rue 12, Médina, Dakar",
            SchoolCity: "Dakar",
            SchoolPhone: "+221338001122",
            SchoolNinea: "123456789",
            SchoolLogoUrl: "https://exemple.sn/logo.png");
    }

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new HourRecordSheetPdfGenerator().Generate(Sheet(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une fiche complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Hours_Are_Recorded_This_Month()
    {
        var pdf = new HourRecordSheetPdfGenerator().Generate(Sheet(lines: new List<HourRecordLineDto>()), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Sheet_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new HourRecordSheetPdfGenerator().Generate(Sheet(), logo: unreadable);

        act.Should().NotThrow();
    }
}
