using System.Text;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// L'outil d'extraction de texte qui sert à PROUVER la confidentialité des PDF d'Internat (lot E). Le flux QuestPDF est
/// compressé et ses glyphes sont indexés : chercher un mot dans les octets ne le trouve jamais, donc une assertion
/// « la fiche médicale est absente » sur les octets bruts passerait toujours sans rien prouver.
/// </summary>
public class PdfTextTests
{
    static PdfTextTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static byte[] Pdf(params string[][] pages) =>
        Document.Create(container =>
        {
            foreach (var lines in pages)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.Content().Column(column =>
                    {
                        foreach (var line in lines)
                        {
                            column.Item().Text(line);
                        }
                    });
                });
            }
        }).GeneratePdf();

    [Fact]
    public void Extract_Returns_The_Text_Drawn_On_The_Page_Accents_Included()
    {
        var text = PdfText.Extract(Pdf(["Allergie sévère aux arachides", "Contact 770000000"]));

        text.Should().Contain("Allergie sévère aux arachides").And.Contain("770000000");
    }

    [Fact]
    public void Extract_Reads_Every_Page()
    {
        var text = PdfText.Extract(Pdf(["Première page"], ["Deuxième page"]));

        text.Should().Contain("Première page").And.Contain("Deuxième page");
    }

    [Fact]
    public void Extract_Does_Not_Invent_Text_That_Is_Not_In_The_Document()
    {
        PdfText.Extract(Pdf(["Nom de l'élève"])).Should().NotContain("Allergie");
    }

    [Fact]
    public void Searching_The_Raw_Bytes_Finds_Nothing_Which_Is_Why_Extraction_Is_Required()
    {
        var bytes = Pdf(["Allergie sévère aux arachides"]);

        Encoding.Latin1.GetString(bytes).Should().NotContain("Allergie",
            "le texte est encodé : un test négatif sur les octets bruts serait vide de sens");
        PdfText.Extract(bytes).Should().Contain("Allergie");
    }
}
