using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Uniforms;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Uniforms;

/// <summary>
/// Module Tenues, Phase 4 — les libellés écrits au journal de stock. <c>stock_movements.Reason</c> est borné à 200
/// caractères et <c>CounterpartyLabel</c> à 150 : un modèle, une taille et un élève aux noms longs ne doivent JAMAIS
/// faire échouer une remise sur une erreur de colonne — le journal est la pièce opposable, la remise ne peut pas
/// dépendre de la longueur d'un nom.
/// </summary>
public class UniformStockLabelsTests
{
    [Fact]
    public void A_Delivery_Reason_Names_The_Item_The_Size_And_The_Student()
    {
        UniformStockLabels.Delivery("Polo bleu", "10 ans", "Awa Fall (ELEV-2026-0001)")
            .Should().Be("Remise de tenue — Polo bleu 10 ans — Awa Fall (ELEV-2026-0001)");
    }

    [Fact]
    public void A_Delivery_Reason_Without_A_Student_Label_Stays_Readable()
    {
        UniformStockLabels.Delivery("Polo bleu", "10 ans", null).Should().Be("Remise de tenue — Polo bleu 10 ans");
    }

    [Fact]
    public void A_Return_Reason_Carries_The_Reason_Given_By_The_Operator()
    {
        UniformStockLabels.Return("Polo bleu", "10 ans", "Mauvaise taille")
            .Should().Be("Retour de tenue — Polo bleu 10 ans — Mauvaise taille");
    }

    [Fact]
    public void The_Longest_Possible_Inputs_Never_Exceed_The_Journal_Column()
    {
        var item = new string('a', UniformCatalogRules.MaxItemNameLength);
        var size = new string('b', UniformCatalogRules.MaxSizeLabelLength);
        var student = new string('c', 220);
        var reason = new string('d', UniformDeliveryPolicy.MaxReasonLength);

        UniformStockLabels.Delivery(item, size, student).Length.Should().BeLessThanOrEqualTo(UniformStockLabels.MaxReasonLength);
        UniformStockLabels.Return(item, size, reason).Length.Should().BeLessThanOrEqualTo(UniformStockLabels.MaxReasonLength);
        UniformStockLabels.Counterparty(student)!.Length.Should().BeLessThanOrEqualTo(UniformStockLabels.MaxCounterpartyLength);
    }

    [Fact]
    public void A_Truncated_Label_Ends_With_An_Ellipsis_And_Keeps_Its_Beginning()
    {
        var label = UniformStockLabels.Delivery("Polo bleu", "10 ans", new string('c', 300));

        label.Should().StartWith("Remise de tenue — Polo bleu 10 ans — ccc");
        label.Should().EndWith("…");
        label.Length.Should().Be(UniformStockLabels.MaxReasonLength);
    }

    [Fact]
    public void A_Label_That_Fits_Is_Left_Untouched()
    {
        UniformStockLabels.Counterparty("Awa Fall").Should().Be("Awa Fall");
        UniformStockLabels.Counterparty(null).Should().BeNull();
        UniformStockLabels.Counterparty("  ").Should().BeNull("un libellé vide n'apprend rien au journal");
    }

    // ------------------------------------------------------------------ état d'une remise

    [Theory]
    [InlineData(StudentUniformStatus.Delivered)]
    [InlineData(StudentUniformStatus.Returned)]
    [InlineData(StudentUniformStatus.Cancelled)]
    public void Only_A_Requested_Line_Is_Deliverable(StudentUniformStatus status)
    {
        var act = () => UniformDeliveryPolicy.EnsureDeliverable(status);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("UNIFORM_NOT_DELIVERABLE");
    }

    [Fact]
    public void A_Requested_Line_Is_Deliverable()
    {
        var act = () => UniformDeliveryPolicy.EnsureDeliverable(StudentUniformStatus.Requested);

        act.Should().NotThrow();
    }
}
