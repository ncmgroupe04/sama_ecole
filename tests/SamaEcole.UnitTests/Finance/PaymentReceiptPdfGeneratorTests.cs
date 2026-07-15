using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Ticket JGK-F02 — le moteur PDF (QuestPDF) produit-il un reçu de paiement valide, sans exception, pour
/// toutes les formes de reçu ? La fidélité visuelle à la référence se vérifie à l'œil ; ces tests couvrent
/// la NON-RÉGRESSION : en-tête PDF correct, robustesse au logo présent puis illisible, et aux coordonnées
/// d'établissement absentes.
/// </summary>
public class PaymentReceiptPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static PaymentReceiptDto Receipt(string? phone = "+221 77 123 45 67", string? city = "Dakar") => new(
        ReceiptNumber: "REC-2025-0007",
        SchoolName: "École Primaire Les Baobabs",
        SchoolPhone: phone,
        SchoolCity: city,
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        Method: "MobileMoney",
        Amount: 30_000m,
        TotalDue: 160_000m,
        AlreadyPaid: 30_000m,
        RemainingBalance: 130_000m,
        PaidAt: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new PaymentReceiptPdfGenerator().Generate(Receipt(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un reçu complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Phone_And_City()
    {
        var pdf = new PaymentReceiptPdfGenerator().Generate(Receipt(phone: null, city: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new PaymentReceiptPdfGenerator().Generate(Receipt(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Receipt_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet de sécurité : des octets pathologiques ne doivent jamais empêcher l'émission du reçu.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new PaymentReceiptPdfGenerator().Generate(Receipt(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new PaymentReceiptPdfGenerator().Generate(Receipt(), logo: unreadable));
    }
}
