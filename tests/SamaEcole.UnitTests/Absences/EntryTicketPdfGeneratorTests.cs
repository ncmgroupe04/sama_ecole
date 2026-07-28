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
        string? observations = null) => new(
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
        SurveillantSignatureUrl: null);

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
