using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Non-régression du moteur PDF (QuestPDF) de la sommation pour impayés.</summary>
public class DuesNoticePdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static DuesNoticeDto Notice(
        string? guardianName = "Moussa Fall",
        IReadOnlyList<DuesNoticeInstallmentDto>? installments = null) => new(
        EnrollmentId: Guid.NewGuid(),
        NoticeNumber: "SOM-1A2B3C4D",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        GuardianName: guardianName,
        GuardianPhone: "+221771234567",
        TotalDue: 150_000,
        AmountPaid: 50_000,
        RemainingBalance: 100_000,
        OverdueInstallments: installments ?? new List<DuesNoticeInstallmentDto>
        {
            new("Frais de scolarité (Mois 1)", 50_000, new DateOnly(2025, 10, 1)),
            new("Frais de scolarité (Mois 2)", 50_000, new DateOnly(2025, 11, 1))
        },
        IssuedAt: new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: "Rue 12, Médina, Dakar",
        SchoolCity: "Dakar",
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
        var pdf = new DuesNoticePdfGenerator().Generate(Notice(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une sommation complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Guardian_Is_Registered()
    {
        var pdf = new DuesNoticePdfGenerator().Generate(Notice(guardianName: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Handles_A_Single_Overdue_Installment()
    {
        var pdf = new DuesNoticePdfGenerator().Generate(
            Notice(installments: new List<DuesNoticeInstallmentDto> { new("Frais d'inscription", 25_000, new DateOnly(2025, 9, 1)) }),
            logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Notice_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new DuesNoticePdfGenerator().Generate(Notice(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
    }
}
