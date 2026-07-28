using FluentAssertions;
using SamaEcole.Application.Finance.Services;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// La TVA se calcule "en dedans" : Amount (Payment/Disbursement) est un montant TTC réel — de l'argent
/// qui a changé de main —, jamais un HT auquel il faudrait ajouter la taxe par-dessus. AGENTS.md interdit
/// de livrer du code Finance sans test associé.
/// </summary>
public class VatCalculatorTests
{
    [Fact]
    public void Null_Rate_Means_Not_Subject_To_Vat_And_Returns_Zero()
    {
        VatCalculator.ComputeVatAmount(100_000m, null).Should().Be(0m,
            "la scolarité (Category par défaut) est typiquement exonérée — null, jamais un taux inventé");
    }

    [Fact]
    public void Zero_Rate_Also_Returns_Zero()
    {
        VatCalculator.ComputeVatAmount(100_000m, 0m).Should().Be(0m);
    }

    [Fact]
    public void Eighteen_Percent_Extracts_The_Correct_Vat_Portion_From_A_Ttc_Amount()
    {
        // 118 TTC à 18% -> 18 exactement (100 HT + 18 TVA = 118 TTC) : cas rond, vérifie le sens du calcul.
        VatCalculator.ComputeVatAmount(118m, 0.18m).Should().Be(18m);
    }

    [Fact]
    public void The_Vat_Amount_Is_Always_Strictly_Less_Than_The_Ttc_Total()
    {
        // Un montant TTC ne peut jamais être ENTIÈREMENT de la TVA — même à un taux élevé, la part HT
        // reste strictement positive.
        var vat = VatCalculator.ComputeVatAmount(100_000m, 0.18m);

        vat.Should().BeLessThan(100_000m);
        vat.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Result_Is_Rounded_To_Two_Decimals_Away_From_Zero()
    {
        // 100 000 * 0.18 / 1.18 = 15254.237... -> arrondi à 15254.24 (AwayFromZero, même convention que
        // ReportCardDocument.FormatGrade).
        VatCalculator.ComputeVatAmount(100_000m, 0.18m).Should().Be(15_254.24m);
    }

    [Theory]
    [InlineData(-0.01)]
    public void A_Negative_Rate_Is_Treated_As_Not_Applicable(decimal negativeRate)
    {
        // Garde défensive : la validation applicative (RuleFor InclusiveBetween(0,1)) empêche déjà un
        // taux négatif d'atteindre ce calculateur, mais celui-ci reste un filet de sécurité pur.
        VatCalculator.ComputeVatAmount(100_000m, negativeRate).Should().Be(0m);
    }

    [Fact]
    public void A_Zero_Ttc_Amount_Produces_Zero_Vat_Even_With_A_Valid_Rate()
    {
        VatCalculator.ComputeVatAmount(0m, 0.18m).Should().Be(0m);
    }
}
