using System.Text;
using FluentAssertions;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Ticket JGK-E02 — le moteur PDF (QuestPDF) produit-il un document valide, sans lever d'exception,
/// pour toutes les formes de reçu ? La fidélité visuelle à la référence se vérifie à l'œil (critère du
/// ticket) ; ces tests couvrent la NON-RÉGRESSION de la génération : en-tête PDF correct, taille non
/// triviale, et robustesse aux cas limites (aucun frais, téléphone/ville absents).
/// </summary>
public class ReceiptPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static EnrollmentReceiptDto Receipt(
        IReadOnlyList<EnrollmentFeeLineDto>? lines = null,
        string? phone = "+221 77 123 45 67",
        string? city = "Dakar") => new(
        EnrollmentId: Guid.NewGuid(),
        ReceiptNumber: "REC-2025-0002",
        SchoolName: "École Primaire Les Baobabs",
        SchoolPhone: phone,
        SchoolCity: city,
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        ClassroomLevel: "Primaire",
        SchoolYearLabel: "2025-2026",
        Type: nameof(EnrollmentType.NewEnrollment),
        Status: nameof(EnrollmentStatus.Confirmed),
        EnrolledAt: new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        Lines: lines ?? new List<EnrollmentFeeLineDto>
        {
            new("Droits d'inscription administrative", IsRecurring: false, UnitAmount: 25_000m, Months: 1, LineTotal: 25_000m),
            new("Mensualité", IsRecurring: true, UnitAmount: 15_000m, Months: 9, LineTotal: 135_000m)
        },
        TotalDue: 160_000m);

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new ReceiptPdfGenerator().Generate(Receipt());

        pdf.Should().NotBeNullOrEmpty();
        pdf.Length.Should().BeGreaterThan(1000, "un reçu complet n'est pas un fichier vide");
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Class_Without_Any_Fees()
    {
        var pdf = new ReceiptPdfGenerator().Generate(
            Receipt(lines: new List<EnrollmentFeeLineDto>()));

        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Phone_And_City()
    {
        var pdf = new ReceiptPdfGenerator().Generate(
            Receipt(phone: null, city: null));

        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-");
    }
}
