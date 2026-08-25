using FluentAssertions;
using SamaEcole.Application.Finance;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Refonte des pièces de caisse (2026-08) — la colonne « Période / Note » du reçu ventilé.
///
/// La règle tient en une cascade : le libellé propre à la ligne, à défaut la période du versement
/// entier, à défaut RIEN. Le dernier maillon est le plus important : une cellule vide est honnête, un
/// tiret ou une période devinée ne l'est pas. Même convention que les coordonnées d'établissement
/// absentes de l'en-tête (docs/design-references/README.md §1.1).
///
/// Ces tests portent sur la logique d'Application, sans instancier QuestPDF (AGENTS.md règle #8) :
/// le générateur PDF ne fait que mettre en page ce que le DTO a déjà résolu.
/// </summary>
public class PaymentReceiptLineLabelTests
{
    private static PaymentReceiptDto Receipt(
        IReadOnlyList<PaymentReceiptLineDto> lines,
        string? referencePeriod = null,
        decimal amount = 30_000m) => new(
        ReceiptNumber: "REC-2026-0007",
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: null,
        SchoolEmail: null,
        SchoolPhone: null,
        SchoolCity: null,
        SchoolNinea: null,
        SchoolRegistreCommerce: null,
        SchoolLogoUrl: null,
        Matricule: "ELEV-2026-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        SchoolYearLabel: "2026-2027",
        Method: "Cash",
        Amount: amount,
        TotalDue: 160_000m,
        AlreadyPaid: amount,
        RemainingBalance: 160_000m - amount,
        PaidAt: new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero),
        Lines: lines,
        ReferencePeriod: referencePeriod);

    [Fact]
    public void A_Line_Label_Wins_Over_The_Payment_Period()
    {
        var line = new PaymentReceiptLineDto("Tenue scolaire", "2 jeux", 5_000m);
        var receipt = Receipt([line], referencePeriod: "Septembre 2026");

        receipt.ResolveLineLabel(line).Should().Be("2 jeux");
    }

    [Fact]
    public void A_Line_Without_Label_Falls_Back_To_The_Payment_Period()
    {
        var line = new PaymentReceiptLineDto("Mensualité scolarité", null, 15_000m);
        var receipt = Receipt([line], referencePeriod: "Septembre 2026");

        receipt.ResolveLineLabel(line).Should().Be("Septembre 2026");
    }

    /// <summary>
    /// Le cas qui compte : sans libellé NI période, la cellule reste vide. Jamais « — », jamais le mois
    /// courant deviné — un reçu est une pièce comptable, il n'invente pas la période qu'il atteste.
    /// </summary>
    [Fact]
    public void A_Line_With_Nothing_To_Show_Resolves_To_Null()
    {
        var line = new PaymentReceiptLineDto("Cantine", null, 5_000m);
        var receipt = Receipt([line], referencePeriod: null);

        receipt.ResolveLineLabel(line).Should().BeNull();
    }

    /// <summary>
    /// Une saisie blanche vaut une absence de saisie, des deux côtés de la cascade — sans quoi un
    /// espace tapé au guichet suffirait à supprimer le repli et à vider la colonne sans raison.
    /// </summary>
    [Theory]
    [InlineData("", "Septembre 2026", "Septembre 2026")]
    [InlineData("   ", "Septembre 2026", "Septembre 2026")]
    [InlineData(null, "   ", null)]
    [InlineData("  ", "", null)]
    public void Blank_Is_Treated_As_Absent_On_Both_Levels(string? label, string? period, string? expected)
    {
        var line = new PaymentReceiptLineDto("Transport", label, 6_000m);
        var receipt = Receipt([line], referencePeriod: period);

        receipt.ResolveLineLabel(line).Should().Be(expected);
    }

    [Fact]
    public void A_Breakdown_Summing_To_The_Amount_Is_Balanced()
    {
        var receipt = Receipt(
            [
                new("Frais d'inscription", "Unique", 10_000m),
                new("Mensualité scolarité", null, 15_000m),
                new("Tenue scolaire", "2 jeux", 5_000m)
            ],
            amount: 30_000m);

        receipt.LinesTotal.Should().Be(30_000m);
        receipt.HasBalancedLines.Should().BeTrue();
    }

    /// <summary>
    /// Ventilation partielle : la caisse n'a imputé qu'une partie du versement. Le reçu ne doit PAS
    /// imprimer ce détail — un tableau dont le total contredit la somme encaissée est un faux.
    /// </summary>
    [Fact]
    public void A_Breakdown_That_Does_Not_Sum_To_The_Amount_Is_Rejected()
    {
        var receipt = Receipt([new("Cantine", null, 5_000m)], amount: 30_000m);

        receipt.HasBalancedLines.Should().BeFalse();
    }

    /// <summary>Aucune ventilation saisie : comportement historique du reçu, ligne unique.</summary>
    [Fact]
    public void An_Empty_Breakdown_Is_Not_Balanced()
    {
        var receipt = Receipt([], amount: 30_000m);

        receipt.LinesTotal.Should().Be(0m);
        receipt.HasBalancedLines.Should().BeFalse();
    }
}
