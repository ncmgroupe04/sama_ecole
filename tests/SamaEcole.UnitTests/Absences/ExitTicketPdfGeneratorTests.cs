using System.Text;
using FluentAssertions;
using SamaEcole.Application.Absences.Queries.GetExitTicket;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Absences;

/// <summary>Non-régression du moteur PDF (QuestPDF) du billet de sortie (A5), pendant du billet d'entrée.</summary>
public class ExitTicketPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static ExitTicketDto Ticket(string? pickedUpBy = "Moussa Fall (père)") => new(
        EarlyDepartureId: Guid.NewGuid(),
        TicketNumber: "SORTIE-1A2B3C4D",
        StudentFullName: "Awa Fall",
        Matricule: "ELEV-2025-0008",
        ClassroomName: "CE1",
        ClassroomLevel: "Primaire",
        Date: new DateTime(2026, 3, 15),
        DepartureTime: new TimeOnly(11, 30),
        Reason: "Rendez-vous médical",
        PickedUpBy: pickedUpBy,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: "Rue 12, Médina, Dakar",
        SchoolPhone: "+221338001122",
        SchoolEmail: "contact@baobabs.sn",
        SchoolCity: "Dakar",
        SchoolNinea: "123456789",
        SchoolRegistreCommerce: null,
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        SurveillantSignatureUrl: null);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new ExitTicketPdfGenerator().Generate(Ticket(), logo: null, qrCodeImage: TinyPng, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(300, "un billet complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Pickup_Person_Is_Recorded()
    {
        var pdf = new ExitTicketPdfGenerator().Generate(Ticket(pickedUpBy: null), logo: null, qrCodeImage: TinyPng, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Surveillant_Signature_Without_Error()
    {
        var pdf = new ExitTicketPdfGenerator().Generate(Ticket(), logo: null, qrCodeImage: TinyPng, surveillantSignature: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Ticket_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ExitTicketPdfGenerator().Generate(Ticket(), logo: unreadable, qrCodeImage: TinyPng, surveillantSignature: null);

        act.Should().NotThrow();
    }
}
