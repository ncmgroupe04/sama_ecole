using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Les trois pièces qui portent la trace d'un paiement annulé (spec 2026-10-17, §2.4) se rendent sans exception :
/// reçu annulé (bandeau + filigrane), journal de clôture (versement signalé « annulé le … » + annulations en négatif)
/// et journal de caisse par date (annulation en négatif). QuestPDF ne lève qu'au rendu : ces tests le déclenchent.
/// </summary>
public class CancelledPaymentPdfTests
{
    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void A_Cancelled_Receipt_Is_Rendered_With_Its_Notice()
    {
        var receipt = new PaymentReceiptDto(
            "REC-2026-0007", "École Primaire Les Baobabs", "123 Rue de l'École", "contact@baobabs.sn", "+221 77 123 45 67",
            "Dakar", "123456789", "SN-DKR-2025-B-1234", "https://exemple.sn/logo.png", "ELEV-2026-0008", "Awa Fall", "CE1",
            "2026-2027", "Cash", 30_000m, 160_000m, 30_000m, 130_000m, new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero),
            [], null, false,
            CancellationNotice: "REÇU ANNULÉ — " + PaymentCancellationOutcomeLabels.CancelledNote(
                new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), "Mauvais élève", PaymentCancellationOutcome.KeptBySchool));

        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(receipt, logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void A_Closing_Report_With_A_Flagged_Payment_And_A_Reversal_Is_Rendered()
    {
        var report = new DailyClosingReportDto(
            SchoolName: "Complexe Scolaire Touba Darou Karim", SchoolAddress: "Touba, Sénégal", SchoolLogoUrl: string.Empty,
            Date: new DateTime(2026, 10, 13), SessionId: "SES-2026-1013-01A0", CashierName: "Cheikh Mbacké Nguirane",
            TimeRange: "08:00 - 16:00", OpeningBalance: 100_000m, TotalCollected: -30_000m, TotalCashInRegister: 120_000m,
            MethodBreakdowns: [new PaymentMethodBreakdownDto(PaymentMethod.Cash, -30_000m)],
            CategoryBreakdowns: [new FeeCategoryBreakdownDto("Scolarité", -30_000m)],
            Transactions:
            [
                new TransactionRowDto("10:00", "REC-2026-0009", "Mamadou Sow", "Tuition", "Cash", 20_000m),
                new TransactionRowDto("11:00", "REC-2026-0010", "Awa Fall", "Tuition", "Cash", 5_000m,
                    CancelledNote: "Annulé le 14/10/2026 — Doublon (simple correction, l'argent reste à l'école)")
            ],
            ActualCashAmount: 120_000m, DiscrepancyAmount: 0m, DiscrepancyReason: null,
            Reversals:
            [
                new ReversalRowDto("13/10 09:00", "REC-2026-0007", "Awa Fall", "Cash", -50_000m,
                    "Annulation du reçu encaissé le 12/10/2026 — Mauvais élève (argent réutilisé pour l'inscription de l'enfant)")
            ],
            TotalReversed: 50_000m);

        var pdf = new DailyClosingReportPdfGenerator(Mock.Of<ILogger<DailyClosingReportPdfGenerator>>()).Generate(report, schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void A_Daily_Cash_Register_With_A_Reversal_Is_Rendered()
    {
        var data = new DailyCashRegisterDto(
            "Complexe Scolaire Touba Darou Karim", "Touba, Sénégal", "contact@touba.sn", "+221 77 123 45 67", null,
            new DateOnly(2026, 10, 13), -30_000m,
            [new DailyCashRegisterMethodTotalDto("Cash", -30_000m)],
            [
                new DailyCashRegisterPaymentDto(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), "Mamadou Sow", "ELEV-2026-0009", "REC-2026-0009", "Cash", 20_000m),
                new DailyCashRegisterPaymentDto(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), "Awa Fall", "ELEV-2026-0008", "REC-2026-0007", "Cash", -50_000m,
                    IsReversal: true, Note: "Annulation du reçu encaissé le 12/10/2026 — Mauvais élève (remboursé au parent)")
            ]);

        var pdf = new DailyCashRegisterPdfGenerator().Generate(data, schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }
}
