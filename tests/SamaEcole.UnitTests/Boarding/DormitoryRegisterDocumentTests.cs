using System.Text;
using FluentAssertions;
using SamaEcole.Application.Boarding.Documents.DormitoryRegister;
using SamaEcole.Infrastructure.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// Registre imprimable d'un pavillon (lot E, tâche 4) — contrôlé sur le TEXTE réellement imprimé. Document posé dans le
/// pavillon : ni donnée de santé ni téléphone. Chaque test « absent » a son jumeau « présent » sur le même document.
/// </summary>
public class DormitoryRegisterDocumentTests
{
    private readonly DormitoryRegisterPdfGenerator _generator = new();

    public DormitoryRegisterDocumentTests() => PdfFonts.EnsureRegistered();

    private static DormitoryRegisterRowDto Row(
        int index, bool? present = null, bool recorded = false, bool onLeave = false, string? note = null, string? nameAr = null) =>
        new($"Chambre {100 + index / 4}", index % 4 + 1, $"Élève n{index:000}", nameAr, $"M-{index:000}", "CM2", onLeave, recorded, present, note);

    private static DormitoryRegisterDto Register(params DormitoryRegisterRowDto[] rows) =>
        new("École A", "Médina, Dakar", null, "2026-2027", "Pavillon Oustaz Ahmad", "M. Ndiaye", new DateOnly(2026, 10, 12),
            rows, new DateTimeOffset(2026, 10, 12, 18, 30, 0, TimeSpan.Zero));

    private string TextOf(DormitoryRegisterDto register) => PdfText.Extract(_generator.Generate(register, null));

    [Fact]
    public void The_Register_Prints_The_Dormitory_The_Night_The_Supervisor_And_Each_Boarder()
    {
        var text = TextOf(Register(Row(0), Row(1)));

        text.Should().Contain("Pavillon Oustaz Ahmad").And.Contain("12/10/2026").And.Contain("M. Ndiaye")
            .And.Contain("Élève n000").And.Contain("M-001").And.Contain("Chambre 100").And.Contain("École A");
    }

    [Fact]
    public void The_Register_Never_Labels_Or_Prints_Health_Or_Phone_Data()
    {
        var text = TextOf(Register(Row(0), Row(1, present: false, recorded: true, note: "Chez sa mère")));

        // Jumeau : le document contient bien ses lignes — l'absence ci-dessous n'est pas celle d'un PDF vide.
        text.Should().Contain("Élève n000").And.Contain("Chez sa mère");
        text.Should().NotContainEquivalentOf("médical").And.NotContainEquivalentOf("téléphone")
            .And.NotContainEquivalentOf("allergie").And.NotContainEquivalentOf("santé");
    }

    [Fact]
    public void A_Recorded_Night_Prints_Present_Or_Absent_And_An_Unrecorded_One_Prints_Neither()
    {
        var recorded = TextOf(Register(Row(0, present: true, recorded: true), Row(1, present: false, recorded: true)));
        var blank = TextOf(Register(Row(0), Row(1)));

        recorded.Should().Contain("Présent").And.Contain("Absent");
        blank.Should().Contain("Élève n000").And.NotContain("Présent").And.NotContain("Absent",
            "les cases à cocher à la main restent vides tant que rien n'est pointé");
    }

    [Fact]
    public void A_Boarder_On_Leave_Is_Flagged_Even_When_The_Note_Is_Custom()
    {
        var text = TextOf(Register(
            Row(0, present: false, recorded: false, onLeave: true, note: "En permission"),
            Row(1, present: false, recorded: true, onLeave: true, note: "Chez sa mère")));

        Regex(text, "En permission").Should().Be(2, "la pastille est posée sur les deux lignes, la note saisie est conservée");
        text.Should().Contain("Chez sa mère");
    }

    [Fact]
    public void A_Long_Register_Spans_Pages_And_Repeats_The_Table_Header_On_Each()
    {
        var rows = Enumerable.Range(0, 120).Select(i => Row(i)).ToArray();

        var pdf = _generator.Generate(Register(rows), null);

        using var document = PdfDocument.Open(pdf);
        document.NumberOfPages.Should().BeGreaterThan(1);
        var text = PdfText.Extract(pdf);
        Regex(text, "Matricule").Should().Be(document.NumberOfPages, "en-tête de tableau répété sur chaque page");
        text.Should().Contain("Élève n000").And.Contain("Élève n119");
    }

    [Fact]
    public void An_Empty_Dormitory_Says_So_Instead_Of_Printing_An_Empty_Table()
    {
        TextOf(Register()).Should().Contain("Aucun pensionnaire").And.Contain("Pavillon Oustaz Ahmad");
    }

    [Fact]
    public void An_Arabic_Name_Adds_An_Arabic_Block_And_A_Bad_Logo_Falls_Back()
    {
        var with = _generator.Generate(Register(Row(0, nameAr: "محمد ديوب")), null);
        var without = _generator.Generate(Register(Row(0)), null);
        var badLogo = _generator.Generate(Register(Row(0)), [1, 2, 3, 4]);

        with.Length.Should().BeGreaterThan(without.Length);
        Encoding.ASCII.GetString(badLogo, 0, 5).Should().Be("%PDF-");
        PdfText.Extract(badLogo).Should().Contain("Élève n000");
    }

    [Fact]
    public void The_Footer_Warns_That_The_Document_Is_Internal()
    {
        TextOf(Register(Row(0))).Should().Contain("Document interne");
    }

    [Fact]
    public void The_Register_Is_Printed_In_Landscape()
    {
        using var document = PdfDocument.Open(_generator.Generate(Register(Row(0)), null));

        var page = document.GetPage(1);
        page.Width.Should().BeGreaterThan(page.Height);
    }

    private static int Regex(string text, string literal) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(literal)).Count;
}
