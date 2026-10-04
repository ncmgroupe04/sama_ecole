using FluentAssertions;
using SamaEcole.Application.Uniforms;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Uniforms;

/// <summary>
/// Module Tenues, Phases 2 et 3 — deux petites règles pures du catalogue :
///   • le NOM du lot d'inventaire qui porte le stock d'une taille (il doit rester lisible dans l'écran Inventaire,
///     et tenir dans les 150 caractères d'un bien) ;
///   • le FILTRE de proposition : quels modèles proposer à un élève selon son sexe et le cycle de sa classe. Une
///     proposition n'est jamais une interdiction : l'école reste libre de remettre n'importe quelle tenue.
/// </summary>
public class UniformCatalogRulesTests
{
    // ------------------------------------------------------------------ nom du lot

    [Fact]
    public void The_Lot_Name_Joins_The_Model_And_The_Size()
    {
        UniformLotNaming.Build("Polo bleu", "10 ans").Should().Be("Polo bleu — 10 ans");
    }

    [Fact]
    public void The_Lot_Name_Trims_Both_Parts()
    {
        UniformLotNaming.Build("  Polo bleu ", " 10 ans  ").Should().Be("Polo bleu — 10 ans");
    }

    [Fact]
    public void The_Longest_Allowed_Name_And_Size_Fit_In_An_Inventory_Item_Name()
    {
        var name = UniformLotNaming.Build(new string('a', UniformCatalogRules.MaxItemNameLength),
            new string('b', UniformCatalogRules.MaxSizeLabelLength));

        name.Length.Should().BeLessThanOrEqualTo(UniformLotNaming.MaxInventoryNameLength,
            "inventory_items.Name est borné à 150 : un lot de tenue ne doit jamais dépasser");
    }

    // ------------------------------------------------------------------ proposition

    [Theory]
    [InlineData(UniformGender.Mixte, "M", true)]
    [InlineData(UniformGender.Mixte, "F", true)]
    [InlineData(UniformGender.Garcon, "M", true)]
    [InlineData(UniformGender.Garcon, "F", false)]
    [InlineData(UniformGender.Fille, "F", true)]
    [InlineData(UniformGender.Fille, "M", false)]
    public void A_Model_Is_Proposed_According_To_The_Gender_Of_The_Student(
        UniformGender itemGender, string studentGender, bool expected)
    {
        UniformProposalRules.Matches(itemGender, itemCycle: null, studentGender, CycleType.Primaire)
            .Should().Be(expected);
    }

    [Fact]
    public void An_Item_Without_A_Cycle_Is_Proposed_To_Every_Cycle()
    {
        UniformProposalRules.Matches(UniformGender.Mixte, null, "F", CycleType.Lycee).Should().BeTrue();
    }

    [Theory]
    [InlineData(CycleType.Primaire, CycleType.Primaire, true)]
    [InlineData(CycleType.College, CycleType.Primaire, false)]
    [InlineData(CycleType.Lycee, CycleType.College, false)]
    public void An_Item_With_A_Cycle_Is_Proposed_Only_To_That_Cycle(
        CycleType itemCycle, CycleType classroomCycle, bool expected)
    {
        UniformProposalRules.Matches(UniformGender.Mixte, itemCycle, "M", classroomCycle).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    public void An_Unknown_Student_Gender_Only_Matches_A_Mixed_Model(string unknown)
    {
        // Le sexe est « M » ou « F » (Student.Gender). Une valeur inattendue ne doit pas faire disparaître les
        // modèles mixtes, ni faire passer un modèle genré pour adapté.
        UniformProposalRules.Matches(UniformGender.Mixte, null, unknown, CycleType.Primaire).Should().BeTrue();
        UniformProposalRules.Matches(UniformGender.Fille, null, unknown, CycleType.Primaire).Should().BeFalse();
        UniformProposalRules.Matches(UniformGender.Garcon, null, unknown, CycleType.Primaire).Should().BeFalse();
    }

    [Theory]
    [InlineData("m")]
    [InlineData("f")]
    public void The_Student_Gender_Is_Read_Case_Insensitively(string lower)
    {
        var expectedBoy = lower == "m";

        UniformProposalRules.Matches(UniformGender.Garcon, null, lower, CycleType.Primaire).Should().Be(expectedBoy);
        UniformProposalRules.Matches(UniformGender.Fille, null, lower, CycleType.Primaire).Should().Be(!expectedBoy);
    }
}
