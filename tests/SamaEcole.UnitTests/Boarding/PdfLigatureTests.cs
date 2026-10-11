using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// Les PDF coupent les ligatures du texte latin : sous Linux la police de repli (Lato) transforme « fi » et « ti » en glyphes sans
/// correspondance Unicode, et « confidentiel » se copie « conﬁden&lt;NUL&gt;el ». Mais l'arabe, lui, DÉPEND de ses ligatures pour sa mise en
/// forme : <see cref="Bilingual.ArabicBlock"/> doit donc les rétablir, quel que soit le style par défaut de la page.
/// </summary>
public class PdfLigatureTests
{
    private const string Arabic = "لا إله إلا الله محمد رسول الله — الأستاذ الفلاني";
    private const string Latin = "Document confidentiel — Aucune sortie enregistrée";

    static PdfLigatureTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static string Extract(bool ligaturesOff)
    {
        SamaEcole.Infrastructure.Documents.PdfFonts.EnsureRegistered();

        var pdf = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.DefaultTextStyle(text => ligaturesOff
                ? text.FontSize(12).DisableFontFeature(FontFeatures.StandardLigatures)
                : text.FontSize(12));
            page.Content().Column(column =>
            {
                column.Item().Element(c => Bilingual.ArabicBlock(c, Arabic));
                column.Item().Text(Latin);
            });
        })).GeneratePdf();

        return PdfText.Extract(pdf);
    }

    [Fact]
    public void Arabic_Block_Renders_The_Same_Whether_Or_Not_The_Page_Disables_Ligatures()
    {
        var on = Extract(ligaturesOff: false);
        var off = Extract(ligaturesOff: true);

        off.Split("Document")[0].Should().Be(on.Split("Document")[0],
            "le bloc arabe doit garder ses ligatures même quand la page coupe celles du latin");
    }

    [Fact]
    public void Latin_Text_Extracts_Cleanly_When_The_Page_Disables_Ligatures()
    {
        Extract(ligaturesOff: true).Should().Contain(Latin);
    }
}
