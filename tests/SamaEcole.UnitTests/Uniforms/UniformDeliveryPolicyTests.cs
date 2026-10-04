using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Uniforms;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Uniforms;

/// <summary>
/// Module Tenues, Phase 1 — la machine à états d'une dotation et les garde-fous de la remise. Pure et sans base :
/// le Handler (Phase 4) l'applique avant de toucher au stock.
///
/// Deux familles de refus, volontairement distinctes comme ailleurs dans le projet :
///   • l'ÉTAT de la dotation interdit l'opération (déjà remise, annulée…) → <see cref="BusinessRuleException"/> (409) ;
///   • la DONNÉE est incomplète (taille absente, motif manquant) → <see cref="ValidationException"/> (422).
/// </summary>
public class UniformDeliveryPolicyTests
{
    private static readonly Guid Taille = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    // ------------------------------------------------------------------ remise

    [Fact]
    public void A_Billed_Delivery_Needs_No_Reason()
    {
        var reason = UniformDeliveryPolicy.ValidateDelivery(
            StudentUniformStatus.Requested, Taille, quantity: 1, UniformBillingStatus.Billed, notBilledReason: null);

        reason.Should().BeNull("une dotation facturée n'a rien à justifier");
    }

    [Fact]
    public void A_Billed_Delivery_Drops_A_Reason_That_Was_Sent_Anyway()
    {
        UniformDeliveryPolicy.ValidateDelivery(
                StudentUniformStatus.Requested, Taille, 1, UniformBillingStatus.Billed, "inutile")
            .Should().BeNull("le motif n'a de sens que pour une remise non facturée");
    }

    [Fact]
    public void A_Not_Billed_Delivery_Requires_A_Reason()
    {
        var act = () => UniformDeliveryPolicy.ValidateDelivery(
            StudentUniformStatus.Requested, Taille, 1, UniformBillingStatus.NotBilled, notBilledReason: null);

        act.Should().Throw<ValidationException>().Which.Errors.Should().ContainKey("notBilledReason");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Reason_Does_Not_Count(string blank)
    {
        var act = () => UniformDeliveryPolicy.ValidateDelivery(
            StudentUniformStatus.Requested, Taille, 1, UniformBillingStatus.NotBilled, blank);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_Not_Billed_Reason_Is_Trimmed()
    {
        UniformDeliveryPolicy.ValidateDelivery(
                StudentUniformStatus.Requested, Taille, 1, UniformBillingStatus.NotBilled, "  Dotation de la mairie  ")
            .Should().Be("Dotation de la mairie");
    }

    [Fact]
    public void A_Reason_Of_The_Maximum_Length_Is_Accepted_And_One_More_Is_Not()
    {
        var max = new string('a', UniformDeliveryPolicy.MaxReasonLength);

        UniformDeliveryPolicy.ValidateDelivery(
                StudentUniformStatus.Requested, Taille, 1, UniformBillingStatus.NotBilled, max)
            .Should().Be(max);

        var act = () => UniformDeliveryPolicy.ValidateDelivery(
            StudentUniformStatus.Requested, Taille, 1, UniformBillingStatus.NotBilled, max + "a");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_Delivery_Needs_A_Confirmed_Size()
    {
        var act = () => UniformDeliveryPolicy.ValidateDelivery(
            StudentUniformStatus.Requested, variantId: null, 1, UniformBillingStatus.Billed, null);

        act.Should().Throw<ValidationException>().Which.Errors.Should().ContainKey("uniformVariantId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Delivery_Needs_A_Positive_Quantity(int quantity)
    {
        var act = () => UniformDeliveryPolicy.ValidateDelivery(
            StudentUniformStatus.Requested, Taille, quantity, UniformBillingStatus.Billed, null);

        act.Should().Throw<ValidationException>().Which.Errors.Should().ContainKey("quantity");
    }

    [Theory]
    [InlineData(StudentUniformStatus.Delivered)]
    [InlineData(StudentUniformStatus.Returned)]
    [InlineData(StudentUniformStatus.Cancelled)]
    public void Only_A_Requested_Line_Can_Be_Delivered(StudentUniformStatus status)
    {
        var act = () => UniformDeliveryPolicy.ValidateDelivery(
            status, Taille, 1, UniformBillingStatus.Billed, null);

        act.Should().Throw<BusinessRuleException>("une tenue déjà remise ne se remet pas : sinon le stock sortirait deux fois");
    }

    // ------------------------------------------------------------------ retour

    [Fact]
    public void A_Delivered_Line_Can_Be_Returned_With_A_Reason()
    {
        UniformDeliveryPolicy.ValidateReturn(StudentUniformStatus.Delivered, "  Mauvaise taille  ")
            .Should().Be("Mauvaise taille");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_Return_Requires_A_Reason(string? reason)
    {
        var act = () => UniformDeliveryPolicy.ValidateReturn(StudentUniformStatus.Delivered, reason);

        act.Should().Throw<ValidationException>().Which.Errors.Should().ContainKey("reason");
    }

    [Theory]
    [InlineData(StudentUniformStatus.Requested)]
    [InlineData(StudentUniformStatus.Returned)]
    [InlineData(StudentUniformStatus.Cancelled)]
    public void Only_A_Delivered_Line_Can_Be_Returned(StudentUniformStatus status)
    {
        var act = () => UniformDeliveryPolicy.ValidateReturn(status, "motif");

        act.Should().Throw<BusinessRuleException>("un retour n'a de sens que sur une tenue réellement remise");
    }

    // ------------------------------------------------------------------ annulation et modification

    [Fact]
    public void A_Requested_Line_Can_Be_Cancelled()
    {
        var act = () => UniformDeliveryPolicy.ValidateCancel(StudentUniformStatus.Requested);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(StudentUniformStatus.Delivered)]
    [InlineData(StudentUniformStatus.Returned)]
    [InlineData(StudentUniformStatus.Cancelled)]
    public void Only_A_Requested_Line_Can_Be_Cancelled(StudentUniformStatus status)
    {
        // Une tenue remise se RETOURNE (mouvement de stock inverse) ; elle ne s'annule pas silencieusement.
        var act = () => UniformDeliveryPolicy.ValidateCancel(status);

        act.Should().Throw<BusinessRuleException>();
    }

    [Theory]
    [InlineData(StudentUniformStatus.Requested, true)]
    [InlineData(StudentUniformStatus.Delivered, false)]
    [InlineData(StudentUniformStatus.Returned, false)]
    [InlineData(StudentUniformStatus.Cancelled, false)]
    public void Only_A_Requested_Line_Can_Be_Edited(StudentUniformStatus status, bool expected)
    {
        UniformDeliveryPolicy.CanEdit(status).Should().Be(expected);
    }

    [Theory]
    [InlineData(StudentUniformStatus.Requested, true)]
    [InlineData(StudentUniformStatus.Delivered, true)]
    [InlineData(StudentUniformStatus.Returned, false)]
    [InlineData(StudentUniformStatus.Cancelled, false)]
    public void Requested_And_Delivered_Are_The_Active_Statuses(StudentUniformStatus status, bool expected)
    {
        // Ce sont ces deux statuts que couvre l'index unique partiel « une ligne active par élève et par modèle ».
        UniformDeliveryPolicy.IsActive(status).Should().Be(expected);
    }
}
