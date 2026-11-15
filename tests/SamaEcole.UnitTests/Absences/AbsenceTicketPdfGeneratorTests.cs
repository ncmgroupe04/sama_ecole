using System.Text;
using FluentAssertions;
using SamaEcole.Application.Absences.Queries.GetAbsenceTicket;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Absences;

/// <summary>Non-régression du moteur PDF (QuestPDF) du billet d'absence (A5), pendant des billets d'entrée et de sortie.</summary>
public class AbsenceTicketPdfGeneratorTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static AbsenceTicketDto Ticket(DateOnly? end = null, DateOnly? back = null) => new(
        AbsenceJustificationId: Guid.NewGuid(),
        TicketNumber: "ABSENCE-1A2B3C4D",
        StudentFullName: "Awa Fall",
        Matricule: "ELEV-2025-0008",
        ClassroomName: "CE1",
        ClassroomLevel: "Primaire",
        StartDate: new DateOnly(2026, 10, 12),
        EndDate: end ?? new DateOnly(2026, 10, 14),
        Days: 3,
        Reason: "Paludisme — certificat médical remis",
        AuthorizedReturnDate: back,
        IssuedOn: new DateOnly(2026, 10, 12),
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: "Rue 12, Médina, Dakar",
        SchoolPhone: "+221338001122",
        SchoolEmail: "contact@baobabs.sn",
        SchoolCity: "Dakar",
        SchoolNinea: "123456789",
        SchoolRegistreCommerce: null,
        SchoolLogoUrl: null,
        SurveillantSignatureUrl: null);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf_For_A_Date_Range()
    {
        var pdf = new AbsenceTicketPdfGenerator().Generate(Ticket(), logo: null, qrCodeImage: TinyPng, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(300);
    }

    [Fact]
    public void Generate_Handles_A_One_Day_Absence_With_An_Authorized_Return_Date()
    {
        var oneDay = Ticket(end: new DateOnly(2026, 10, 12), back: new DateOnly(2026, 10, 13)) with { Days = 1 };

        ShouldBeAValidPdf(new AbsenceTicketPdfGenerator().Generate(oneDay, logo: null, qrCodeImage: TinyPng, surveillantSignature: TinyPng));
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Ticket_When_Logo_Bytes_Are_Unreadable()
    {
        var act = () => new AbsenceTicketPdfGenerator().Generate(Ticket(), logo: new byte[] { 1, 2, 3, 4 }, qrCodeImage: TinyPng, surveillantSignature: null);

        act.Should().NotThrow();
    }
}
