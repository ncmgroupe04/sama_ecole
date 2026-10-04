using FluentAssertions;
using SamaEcole.Application.Uniforms;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Uniforms;

/// <summary>
/// Module Tenues, Phase 1 — la règle qui décide si une dotation est « facturée ». Elle ne lit que ce que Finance
/// a déjà posé (les lignes de frais de l'inscription) : le module n'écrit jamais dans Finance (règle #4).
///
/// Une dotation est <see cref="UniformBillingStatus.Billed"/> si et seulement si :
///   1. le modèle de tenue est rattaché à une catégorie de frais ;
///   2. l'inscription porte une ligne de frais de cette catégorie (la famille a coché le frais optionnel) ;
///   3. la quantité ne dépasse pas ce que la ligne couvre (<c>QuantityIncluded</c>).
/// </summary>
public class UniformBillingCheckTests
{
    private static readonly Guid FraisUniforme = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid FraisSport = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Mensualite = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    [Fact]
    public void Is_Billed_When_The_Enrollment_Carries_The_Fee_Line_Of_The_Item()
    {
        UniformBillingCheck.Evaluate(FraisUniforme, quantityIncluded: 1, quantity: 1, [FraisUniforme, Mensualite])
            .Should().Be(UniformBillingStatus.Billed);
    }

    [Fact]
    public void Is_Not_Billed_When_The_Family_Did_Not_Tick_The_Optional_Fee()
    {
        // Frais optionnel décoché à l'inscription : aucune ligne de cette catégorie.
        UniformBillingCheck.Evaluate(FraisUniforme, quantityIncluded: 1, quantity: 1, [Mensualite])
            .Should().Be(UniformBillingStatus.NotBilled);
    }

    [Fact]
    public void Is_Not_Billed_When_The_Fee_Line_Is_Of_Another_Category()
    {
        UniformBillingCheck.Evaluate(FraisUniforme, quantityIncluded: 1, quantity: 1, [FraisSport])
            .Should().Be(UniformBillingStatus.NotBilled);
    }

    [Fact]
    public void Is_Not_Billed_When_The_Item_Is_Not_Linked_To_A_Fee_Category()
    {
        UniformBillingCheck.Evaluate(feeCategoryId: null, quantityIncluded: 1, quantity: 1, [FraisUniforme])
            .Should().Be(UniformBillingStatus.NotBilled);
    }

    [Fact]
    public void Is_Not_Billed_Beyond_The_Quantity_Covered_By_The_Fee_Line()
    {
        // Le frais couvre 2 polos : un troisième exemplaire n'est pas facturé.
        UniformBillingCheck.Evaluate(FraisUniforme, quantityIncluded: 2, quantity: 3, [FraisUniforme])
            .Should().Be(UniformBillingStatus.NotBilled);
    }

    [Fact]
    public void Is_Billed_Up_To_The_Quantity_Covered_Inclusive()
    {
        UniformBillingCheck.Evaluate(FraisUniforme, quantityIncluded: 2, quantity: 2, [FraisUniforme])
            .Should().Be(UniformBillingStatus.Billed);
    }

    [Fact]
    public void Is_Not_Billed_When_The_Enrollment_Has_No_Fee_Line_At_All()
    {
        UniformBillingCheck.Evaluate(FraisUniforme, quantityIncluded: 1, quantity: 1, [])
            .Should().Be(UniformBillingStatus.NotBilled);
    }
}
