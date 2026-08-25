using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
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

    private static PaymentReceiptDto Receipt(
        string? phone = "+221 77 123 45 67",
        string? city = "Dakar",
        bool isAcceleratedClass = false,
        string classroomName = "CE1",
        IReadOnlyList<PaymentReceiptLineDto>? lines = null,
        string? referencePeriod = null,
        decimal amount = 30_000m) => new(
        ReceiptNumber: "REC-2025-0007",
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: "123 Rue de l'École",
        SchoolEmail: "contact@baobabs.sn",
        SchoolPhone: phone,
        SchoolCity: city,
        SchoolNinea: "123456789",
        SchoolRegistreCommerce: "SN-DKR-2025-B-1234",
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: classroomName,
        SchoolYearLabel: "2025-2026",
        Method: "MobileMoney",
        Amount: amount,
        TotalDue: 160_000m,
        AlreadyPaid: amount,
        RemainingBalance: 160_000m - amount,
        PaidAt: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero),
        Lines: lines ?? [],
        ReferencePeriod: referencePeriod,
        IsAcceleratedClass: isAcceleratedClass);

    /// <summary>Ventilation qui BALANCE : la somme des lignes fait exactement les 30 000 encaissés.</summary>
    private static IReadOnlyList<PaymentReceiptLineDto> BalancedLines() =>
    [
        new("Frais d'inscription", "Unique", 10_000m),
        new("Mensualité scolarité", null, 15_000m),   // repli sur ReferencePeriod
        new("Tenue scolaire", "2 jeux", 5_000m)
    ];

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un reçu complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Phone_And_City()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(phone: null, city: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    /// <summary>
    /// Reçu de caisse d'un élève de classe passerelle : même mention que sur le reçu d'inscription qu'il
    /// complète. Couverture de NON-RÉGRESSION du rendu ; la composition du libellé elle-même est couverte
    /// par ClassroomPromotionTests (aucun extracteur de texte PDF n'est référencé dans ce projet).
    /// </summary>
    [Fact]
    public void Generate_Is_Robust_To_An_Accelerated_Bridge_Class()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(isAcceleratedClass: true, classroomName: "CI-CP"), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000);
    }

    /// <summary>
    /// Reçu VENTILÉ (refonte 2026-08) : une ligne par poste imputé, dont une sans libellé propre qui
    /// doit retomber sur la période du versement. Couverture de non-régression du rendu ; la résolution
    /// du libellé elle-même est couverte par PaymentReceiptLineLabelTests.
    /// </summary>
    [Fact]
    public void Generate_Renders_A_Ventilated_Receipt()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(lines: BalancedLines(), referencePeriod: "Septembre 2026"), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000);
    }

    /// <summary>
    /// Ventilation qui NE BALANCE PAS : le document doit se replier sur sa ligne unique plutôt que
    /// d'imprimer un détail dont le total contredit le montant encaissé. Un reçu qui ne balance pas
    /// est comptablement invalide — mieux vaut moins de détail qu'un faux.
    /// </summary>
    [Fact]
    public void Generate_Is_Robust_To_An_Unbalanced_Breakdown()
    {
        IReadOnlyList<PaymentReceiptLineDto> unbalanced = [new("Cantine", null, 1_000m)];

        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(lines: unbalanced), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    /// <summary>
    /// Depuis la ventilation, le tableau du reçu a une longueur VARIABLE — il lui faut donc la même
    /// garde que l'attestation (voir ReceiptPdfGeneratorTests) : un versement imputé sur 8 postes doit
    /// rester sur une seule page A5 paysage. Le nombre de pages se lit dans l'objet racine
    /// `/Type /Pages /Count N`, aucune bibliothèque d'extraction PDF n'étant référencée ici.
    /// </summary>
    [Fact]
    public void Generate_Stays_On_A_Single_Page_With_Many_Ventilated_Lines()
    {
        var many = Enumerable.Range(1, 8)
            .Select(i => new PaymentReceiptLineDto($"Poste réglé n° {i}", i % 2 == 0 ? null : $"Note {i}", 1_000m * i))
            .ToList();

        // 1+2+…+8 = 36 milliers : le total doit correspondre, sinon le document se replie sur sa
        // ligne unique et le test ne prouverait plus rien sur la forme ventilée.
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(amount: 36_000m, lines: many, referencePeriod: "Septembre 2026"), logo: null);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1",
            "un versement imputé sur beaucoup de postes doit rester sur une seule page A5 paysage");
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Receipt_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet de sécurité : des octets pathologiques ne doivent jamais empêcher l'émission du reçu.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable));
    }
}
