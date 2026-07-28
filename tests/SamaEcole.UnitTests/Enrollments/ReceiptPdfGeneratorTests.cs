using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Ticket JGK-E02 — le moteur PDF (QuestPDF) produit-il un document valide, sans lever d'exception,
/// pour toutes les formes de reçu ? La fidélité visuelle à la référence se vérifie à l'œil (critère du
/// ticket) ; ces tests couvrent la NON-RÉGRESSION de la génération : en-tête PDF correct, taille non
/// triviale, et robustesse aux cas limites (aucun frais, téléphone/ville absents, logo présent ou illisible).
/// </summary>
public class ReceiptPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // PNG 1×1 transparent, valide — de quoi exercer l'incrustation réelle du logo sans dépendre du réseau.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static EnrollmentReceiptDto Receipt(
        IReadOnlyList<EnrollmentFeeLineDto>? lines = null,
        string? phone = "+221 77 123 45 67",
        string? city = "Dakar",
        IReadOnlyList<CollectedFeeLineDto>? collected = null,
        string? legalMentions = "SN-DKR-2020-B-1234") => new(
        EnrollmentId: Guid.NewGuid(),
        ReceiptNumber: "REC-2025-0002",
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolPhone: phone,
        SchoolEmail: legalMentions is null ? null : "contact@baobabs.sn",
        SchoolNinea: legalMentions,
        SchoolRegistreCommerce: legalMentions,
        SchoolCity: city,
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        ClassroomLevel: "Primaire",
        SchoolYearLabel: "2025-2026",
        GuardianName: legalMentions is null ? null : "Ndèye Fall",
        GuardianPhone: legalMentions is null ? null : "77 000 00 00",
        Type: nameof(EnrollmentType.NewEnrollment),
        Status: nameof(EnrollmentStatus.Confirmed),
        EnrolledAt: new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        Lines: lines ?? new List<EnrollmentFeeLineDto>
        {
            new("Droits d'inscription administrative", IsRecurring: false, UnitAmount: 25_000m, Months: 1, LineTotal: 25_000m),
            new("Mensualité", IsRecurring: true, UnitAmount: 15_000m, Months: 9, LineTotal: 135_000m)
        },
        TotalDue: 160_000m,
        CollectedLines: collected ?? new List<CollectedFeeLineDto>
        {
            new("Droits d'inscription administrative", IsRecurring: false, Months: 1, Amount: 25_000m),
            new("Mensualité", IsRecurring: true, Months: 1, Amount: 15_000m)
        },
        TotalCollected: collected?.Sum(c => c.Amount) ?? 40_000m,
        PaymentMethod: nameof(PaymentMethod.Cash));

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un reçu complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Class_Without_Any_Fees()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(
            Receipt(lines: new List<EnrollmentFeeLineDto>()), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Phone_And_City()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(
            Receipt(phone: null, city: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Legal_Mentions_And_Guardian()
    {
        // Une école qui n'a pas encore saisi NINEA/RCCM ni le tuteur doit quand même pouvoir imprimer :
        // la mention absente ne s'imprime pas, elle ne fait pas tomber le reçu.
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(legalMentions: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_An_Enrollment_Without_Any_Collection()
    {
        // Dossier ouvert sans versement : le reçu s'imprime avec un total encaissé de 0.
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(
            Receipt(collected: new List<CollectedFeeLineDto>()), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: TinyPng);

        // On ne peut pas « voir » l'image en test unitaire, mais on couvre la branche d'incrustation :
        // un logo valide doit produire un PDF valide, sans exception.
        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Receipt_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet de sécurité : des octets pathologiques (ni PNG ni JPEG décodable) ne doivent JAMAIS
        // empêcher l'émission du reçu officiel — le générateur régénère alors sans le logo.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable));
    }
}
