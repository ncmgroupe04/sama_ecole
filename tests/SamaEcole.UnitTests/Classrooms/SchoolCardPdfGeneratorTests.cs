using System.Text;
using FluentAssertions;
using SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Classrooms;

/// <summary>
/// Non-régression du moteur PDF (QuestPDF) des cartes scolaires. Le logo arrive désormais en octets,
/// récupéré une seule fois par le handler via ISchoolLogoProvider (protégé contre le SSRF) : le
/// document ne fait plus aucun appel réseau lui-même.
/// </summary>
public class SchoolCardPdfGeneratorTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static SchoolCardBatchDto Batch(byte[]? logo, int cardCount) => new(
        SchoolName: "École Primaire Les Baobabs",
        SchoolLogo: logo,
        ClassroomName: "CM2 A",
        SchoolYearName: "2026-2027",
        PhoneNumber: "+221338001122",
        Address: "Rue 12, Médina, Dakar",
        Cards: Enumerable.Range(1, cardCount)
            .Select(i => new SchoolCardDto($"Élève {i}", $"ELEV-2026-{i:0000}", new DateOnly(2015, 1, 1), "Dakar", TinyPng))
            .ToList());

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Theory]
    [InlineData(1)]  // une rangée incomplète : la seconde colonne reste vide
    [InlineData(11)] // plus d'une page (10 cartes par page), dernière rangée incomplète
    public void Generate_Produces_A_Valid_Pdf_Without_Logo(int cardCount)
    {
        var pdf = new SchoolCardPdfGenerator().GeneratePdf(Batch(logo: null, cardCount));

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new SchoolCardPdfGenerator().GeneratePdf(Batch(TinyPng, cardCount: 2));

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_Logoless_Cards_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var pdf = new SchoolCardPdfGenerator().GeneratePdf(Batch(unreadable, cardCount: 2));

        ShouldBeAValidPdf(pdf);
    }
}
