using System.Text;
using FluentAssertions;
using SamaEcole.Application.VieScolaire.Queries.GetParentNotice;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.VieScolaire;

/// <summary>Non-régression du moteur PDF (QuestPDF) de la convocation de parent.</summary>
public class ParentNoticePdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static ParentNoticeDto Notice(
        string? guardianName = "Moussa Fall",
        string? city = "Dakar") => new(
        ParentSummonsId: Guid.NewGuid(),
        NoticeNumber: "CONV-1A2B3C4D",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        ScheduledAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero),
        Reason: "Baisse des résultats scolaires ce trimestre",
        IssuedAt: new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero),
        GuardianName: guardianName,
        GuardianPhone: "+221771234567",
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolCity: city,
        InspectionAcademie: "Dakar",
        InspectionEducationFormation: "Dakar 1",
        HeadingPrefix: "ÉCOLE ÉLÉMENTAIRE DE",
        HeadingName: "Popenguine",
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new ParentNoticePdfGenerator().Generate(Notice(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une convocation complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Guardian_Is_Registered()
    {
        var pdf = new ParentNoticePdfGenerator().Generate(Notice(guardianName: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_City()
    {
        var pdf = new ParentNoticePdfGenerator().Generate(Notice(city: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Notice_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ParentNoticePdfGenerator().Generate(Notice(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
    }
}
