using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Non-régression du moteur PDF (QuestPDF) du rapport de clôture de caisse (JGK-F09). Ce générateur
/// n'avait AUCUN test : il a longtemps échoué en silence faute de licence QuestPDF, puis renvoyait le
/// document brut sans filet. On couvre ici : PDF valide et non trivial, robustesse à un logo illisible
/// (repli sans logo, comme les autres pièces officielles), et session sans mouvement (0 transaction).
/// </summary>
public class DailyClosingReportPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static DailyClosingReportPdfGenerator NewGenerator() =>
        new(Mock.Of<ILogger<DailyClosingReportPdfGenerator>>());

    private static DailyClosingReportDto Report(
        IReadOnlyList<TransactionRowDto>? transactions = null,
        decimal? actualCashAmount = 0m) => new(
        SchoolName: "Complexe Scolaire Touba Darou Karim",
        SchoolAddress: "Touba, Sénégal",
        SchoolLogoUrl: string.Empty,
        Date: new DateTime(2026, 8, 31),
        SessionId: "SES-2026-0831-01A0",
        CashierName: "Cheikh Mbacké Nguirane",
        TimeRange: "08:00 - 16:00",
        OpeningBalance: 25_000m,
        TotalCollected: 80_000m,
        TotalCashInRegister: 105_000m,
        MethodBreakdowns: [new PaymentMethodBreakdownDto(PaymentMethod.Cash, 80_000m)],
        CategoryBreakdowns: [new FeeCategoryBreakdownDto("Scolarité", 80_000m)],
        Transactions: (transactions ??
        [
            new TransactionRowDto("09:15", "REC-2025-0007", "Awa Fall", "Scolarité", "Cash", 50_000m),
            new TransactionRowDto("11:40", "REC-2025-0008", "Mamadou Sow", "Scolarité", "Cash", 30_000m)
        ]).ToList(),
        ActualCashAmount: actualCashAmount,
        DiscrepancyAmount: actualCashAmount is null ? null : actualCashAmount - 105_000m,
        DiscrepancyReason: null);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = NewGenerator().Generate(Report(), schoolLogo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un rapport de clôture complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Session_Without_Any_Transaction()
    {
        var pdf = NewGenerator().Generate(Report(transactions: []), schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Report_Still_Open_Without_Physical_Count()
    {
        var pdf = NewGenerator().Generate(Report(actualCashAmount: null), schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Report_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet ajouté avec les autres pièces officielles : des octets pathologiques en guise de
        // logo ne doivent jamais empêcher l'émission du rapport de clôture.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => NewGenerator().Generate(Report(), schoolLogo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(NewGenerator().Generate(Report(), schoolLogo: unreadable));
    }
}
