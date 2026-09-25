using System.Text;
using FluentAssertions;
using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Absences;

/// <summary>
/// Le moteur PDF (QuestPDF) du billet d'entrée A5 produit-il un document valide, sans lever
/// d'exception, y compris aux cas limites (école sans téléphone/ville/mentions légales, logo présent
/// ou illisible) ? La fidélité visuelle se vérifie à l'œil ; ces tests couvrent la NON-RÉGRESSION de
/// la génération et sa robustesse — un billet doit toujours pouvoir s'imprimer.
/// </summary>
public class EntryTicketPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // PNG 1×1 transparent, valide — exerce l'incrustation réelle du logo sans dépendre du réseau.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static EntryTicketDto Ticket(
        string? phone = "+221 77 123 45 67",
        string? city = "Dakar",
        string? legalMentions = "SN-DKR-2020-B-1234",
        string? observations = null,
        string? subject = null,
        string? status = null) => new(
        LateArrivalId: Guid.NewGuid(),
        TicketNumber: "BILLET-1A2B3C4D",
        IssuedAt: new DateTimeOffset(2026, 10, 1, 8, 15, 0, TimeSpan.Zero),
        StudentFullName: "Awa Fall",
        Matricule: "ELEV-2025-0008",
        ClassroomName: "CE1",
        ClassroomLevel: "Primaire",
        Date: new DateTime(2026, 10, 1),
        Minutes: 15,
        Reason: "Transport en commun bloqué",
        Observations: observations,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolPhone: phone,
        SchoolEmail: legalMentions is null ? null : "contact@baobabs.sn",
        SchoolCity: city,
        SchoolNinea: legalMentions,
        SchoolRegistreCommerce: legalMentions,
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        SurveillantSignatureUrl: null,
        TargetSubjectName: subject,
        TargetTimeRange: subject is null ? null : "08:00-10:00",
        TargetTeacherName: subject is null ? null : "Mme Awa Sow",
        Status: status);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new EntryTicketPdfGenerator().Generate(Ticket(), logo: null, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un billet complet n'est pas un fichier vide");
    }

    // Évolution N°5 : le billet d'un cours visé porte le cours et son statut, et reste UNE page A5.
    [Theory]
    [InlineData("Issued")]
    [InlineData("Accepted")]
    [InlineData("Cancelled")]
    public void Generate_Prints_A_Targeted_Ticket_In_Every_Status_On_A_Single_Page(string status)
    {
        var pdf = new EntryTicketPdfGenerator().Generate(
            Ticket(subject: "Mathématiques", status: status), logo: null, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1", "le billet A5 ne doit jamais déborder sur une deuxième page");
    }

    // Complément N°5 bis : le billet par heure d'arrivée détaille l'heure, la durée et les cours manqués — sur UNE page.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(9)]
    public void Generate_Prints_An_Arrival_Time_Ticket_On_A_Single_Page_Whatever_The_Number_Of_Missed_Courses(int missed)
    {
        var slots = Enumerable.Range(0, missed)
            .Select(i => new EntryTicketMissedSlot($"Matière {i + 1}", $"{8 + i:00}:00-{9 + i:00}:00", 60))
            .ToList();
        var ticket = Ticket(subject: "Français", status: "Issued") with
        {
            ArrivalTime = new TimeOnly(10, 20), TotalMinutes = missed * 60 + 20, Minutes = 20, MissedSlots = slots
        };

        var pdf = new EntryTicketPdfGenerator().Generate(ticket, logo: null, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1");
    }

    [Fact]
    public void Generate_Prints_An_Arrival_Ticket_With_No_Late_Minutes()
    {
        var ticket = Ticket(subject: "Français", status: "Issued") with
        {
            ArrivalTime = new TimeOnly(10, 0), TotalMinutes = 120, Minutes = 0,
            MissedSlots = [new EntryTicketMissedSlot("Mathématiques", "08:00-10:00", 120)]
        };

        ShouldBeAValidPdf(new EntryTicketPdfGenerator().Generate(ticket, logo: null, surveillantSignature: null));
    }

    [Fact]
    public void Generate_Stays_On_A_Single_Page_With_A_Long_Subject_And_Long_Observations()
    {
        var pdf = new EntryTicketPdfGenerator().Generate(
            Ticket(
                observations: "Élève accompagné de son tuteur, retard dû à une panne de transport en commun sur la corniche ouest.",
                subject: "Sciences de la vie et de la terre — travaux pratiques et expérimentation en laboratoire",
                status: "Issued"),
            logo: null, surveillantSignature: null);

        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Phone_And_City()
    {
        var pdf = new EntryTicketPdfGenerator().Generate(Ticket(phone: null, city: null), logo: null, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Legal_Mentions()
    {
        // Une école qui n'a pas encore saisi NINEA/RCCM doit quand même pouvoir imprimer un billet :
        // la mention absente ne s'imprime pas, elle ne fait pas tomber le document.
        var pdf = new EntryTicketPdfGenerator().Generate(Ticket(legalMentions: null), logo: null, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Provided_Observations_Note()
    {
        var pdf = new EntryTicketPdfGenerator().Generate(
            Ticket(observations: "Avertissement verbal donné. Au prochain retard, convocation des parents."),
            logo: null, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new EntryTicketPdfGenerator().Generate(Ticket(), logo: TinyPng, surveillantSignature: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Surveillant_Signature_Without_Error()
    {
        var pdf = new EntryTicketPdfGenerator().Generate(Ticket(), logo: null, surveillantSignature: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Ticket_When_Logo_Bytes_Are_Unreadable()
    {
        // Des octets pathologiques (ni PNG ni JPEG décodable) ne doivent JAMAIS empêcher l'émission
        // du billet — le générateur régénère alors sans le logo.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new EntryTicketPdfGenerator().Generate(Ticket(), logo: unreadable, surveillantSignature: null);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new EntryTicketPdfGenerator().Generate(Ticket(), logo: unreadable, surveillantSignature: null));
    }
}
