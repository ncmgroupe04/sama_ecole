using System.Text;
using FluentAssertions;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Documents.BoarderSheet;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// Fiche individuelle de pensionnaire en PDF (lot E, tâche 3) — contrôlée sur le TEXTE réellement imprimé. Chaque test
/// « absent » a son jumeau « présent » sur le même document : l'absence ne prouve rien si le PDF est vide.
/// </summary>
public class BoarderSheetDocumentTests
{
    private const string MedicalSecret = "Allergie sévère aux arachides (EpiPen)";

    private readonly BoarderSheetPdfGenerator _generator = new();

    public BoarderSheetDocumentTests() => PdfFonts.EnsureRegistered();

    private static BoardingLeaveDto Leave(bool unlisted, LeaveStatus status, DateOnly? returned = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Élève m1", "M-m1", "Pavillon Oustaz Ahmad", "Chambre 101",
            BoardingLeaveReason.Famille, null, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 14), returned,
            "Oncle Moussa", unlisted, status, 1);

    private static BoarderSheetDto Sheet(
        string? medical = MedicalSecret, bool ended = false, string? nameAr = null, IReadOnlyList<BoardingLeaveDto>? leaves = null) =>
        new("École A", "Médina, Dakar", null, "2026-2027",
            new BoarderListItemDto(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Élève m1", "M-m1", "CM2", BoardingRegime.Interne, !ended,
                ended ? null : Guid.NewGuid(), ended ? null : 1, ended ? null : Guid.NewGuid(), ended ? null : "Chambre 101",
                ended ? null : Guid.NewGuid(), ended ? null : "Pavillon Oustaz Ahmad",
                new DateOnly(2026, 9, 15), ended ? new DateOnly(2026, 10, 1) : null, 1),
            nameAr, new DateOnly(2014, 1, 1), "Dakar", medical, "Mme Diop", "77 123 45 67",
            [new AllowedExitPersonDto("Fatou Diop", "Mère", "77 000 00 00")], leaves ?? [],
            new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));

    private string TextOf(BoarderSheetDto sheet, byte[]? logo = null) => PdfText.Extract(_generator.Generate(sheet, logo));

    [Fact]
    public void The_Director_Sheet_Prints_The_Identity_The_Housing_The_Contacts_And_The_Medical_Notes()
    {
        var text = TextOf(Sheet());

        text.Should().Contain("Élève m1").And.Contain("M-m1").And.Contain("CM2")
            .And.Contain("Pavillon Oustaz Ahmad").And.Contain("Chambre 101")
            .And.Contain("Mme Diop").And.Contain("77 123 45 67").And.Contain("Fatou Diop")
            .And.Contain("École A");
        text.Should().ContainEquivalentOf("fiche médicale").And.Contain(MedicalSecret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_Medical_Notes_The_Sheet_Has_Neither_The_Value_Nor_The_Block(string? notes)
    {
        var text = TextOf(Sheet(medical: notes));

        // Jumeau : le MÊME document imprime bien le reste — l'absence ci-dessous n'est pas celle d'un PDF vide.
        text.Should().Contain("Élève m1").And.Contain("M-m1").And.Contain("Pavillon Oustaz Ahmad").And.Contain("Mme Diop");
        text.Should().NotContainEquivalentOf("fiche médicale").And.NotContain("Allergie").And.NotContain("EpiPen");
    }

    [Fact]
    public void An_Ended_Stay_Says_So_And_Has_No_Bed_Line()
    {
        var text = TextOf(Sheet(ended: true));

        text.Should().Contain("Séjour clos le 01/10/2026").And.Contain("Élève m1");
        text.Should().NotContain("Pavillon Oustaz Ahmad");
    }

    [Fact]
    public void An_Active_Stay_Does_Not_Claim_To_Be_Ended()
    {
        TextOf(Sheet()).Should().NotContain("Séjour clos");
    }

    [Fact]
    public void The_Leave_History_Flags_An_Unlisted_Companion_And_Shows_Each_Status()
    {
        var leaves = new[]
        {
            Leave(unlisted: true, LeaveStatus.Active),
            Leave(unlisted: false, LeaveStatus.Returned, returned: new DateOnly(2026, 9, 22))
        };

        var text = TextOf(Sheet(leaves: leaves));

        text.Should().Contain("Non habilité").And.Contain("En cours").And.Contain("Rentré").And.Contain("Oncle Moussa");
        Regex(text, "Non habilité").Should().Be(1, "seule la sortie avec accompagnateur hors liste porte la pastille");
    }

    [Fact]
    public void Without_Leaves_The_Sheet_States_It_Instead_Of_Printing_An_Empty_Table()
    {
        TextOf(Sheet()).Should().Contain("Aucune sortie enregistrée");
    }

    [Fact]
    public void An_Arabic_Name_Adds_An_Arabic_Block_And_Its_Absence_Prints_Nothing()
    {
        var withArabic = _generator.Generate(Sheet(nameAr: "محمد ديوب"), null);
        var without = _generator.Generate(Sheet(), null);

        withArabic.Length.Should().BeGreaterThan(without.Length, "la police arabe embarquée est ajoutée au document");
        Encoding.ASCII.GetString(without, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void A_Missing_Or_Unreadable_Logo_Falls_Back_To_A_Sheet_Without_Logo()
    {
        var withBadLogo = _generator.Generate(Sheet(), [1, 2, 3, 4]);

        Encoding.ASCII.GetString(withBadLogo, 0, 5).Should().Be("%PDF-");
        PdfText.Extract(withBadLogo).Should().Contain("Élève m1");
    }

    [Fact]
    public void The_Footer_Marks_The_Document_As_Confidential_With_Its_Print_Date()
    {
        TextOf(Sheet()).Should().Contain("Document confidentiel").And.Contain("12/10/2026");
    }

    private static int Regex(string text, string literal) => System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(literal)).Count;
}
