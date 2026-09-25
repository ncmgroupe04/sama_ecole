using FluentAssertions;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Pré-cochage automatique de la rangée des distinctions du bulletin d'après la moyenne générale.
/// Moitié HAUTE seulement : Félicitations / Tableau d'honneur / Encouragements récompensent un chiffre
/// et se déduisent sans injustice ; Blâme et Avertissement sanctionnent un comportement et restent la
/// décision du conseil. Voir DisciplinaryMentionPolicy.
/// </summary>
public class DisciplinaryMentionPolicyTests
{
    [Theory]
    [InlineData(17.0, DisciplinaryMention.Felicitations)]
    [InlineData(14.0, DisciplinaryMention.Felicitations)]
    [InlineData(13.99, DisciplinaryMention.TableauHonneur)]
    [InlineData(12.0, DisciplinaryMention.TableauHonneur)]
    public void An_Award_Is_Proposed_From_Twelve_On(double average, DisciplinaryMention expected)
    {
        // Règles MEN (Évolution N°7) : Félicitations ≥ 14, Tableau d'honneur ≥ 12 sans note éliminatoire.
        DisciplinaryMentionPolicy.Suggest((decimal)average, 20, hasGrades: true).Should().Be(expected);
    }

    [Theory]
    [InlineData(13.5, DisciplinaryMention.Encouragements)]
    [InlineData(12.0, DisciplinaryMention.Encouragements)]
    [InlineData(15.0, DisciplinaryMention.Felicitations)]
    public void An_Eliminatory_Grade_Turns_The_Honour_Roll_Into_Encouragements(double average, DisciplinaryMention expected)
        => DisciplinaryMentionPolicy.Suggest((decimal)average, 20, hasGrades: true, hasEliminatoryGrade: true)
            .Should().Be(expected);

    [Fact]
    public void The_School_Thresholds_Are_Honoured()
    {
        var strict = CouncilRules.Default with { FelicitationsMin = 16, HonorRollMin = 14, EncouragementsMin = 13 };

        DisciplinaryMentionPolicy.Suggest(15m, 20, hasGrades: true, strict).Should().Be(DisciplinaryMention.TableauHonneur);
        DisciplinaryMentionPolicy.Suggest(13.5m, 20, hasGrades: true, strict).Should().Be(DisciplinaryMention.Encouragements);
        DisciplinaryMentionPolicy.Suggest(12.5m, 20, hasGrades: true, strict).Should().BeNull();
    }

    [Theory]
    [InlineData(11.99)]
    [InlineData(8.0)]
    [InlineData(2.0)]
    [InlineData(0.0)]
    public void Below_Twelve_Nothing_Is_Proposed_Never_A_Sanction(double average)
    {
        DisciplinaryMentionPolicy.Suggest((decimal)average, 20, hasGrades: true).Should().BeNull();
    }

    [Fact]
    public void A_Card_Without_Any_Grade_Gets_No_Proposal()
    {
        // Moyenne générale de 0 par convention sur un ensemble vide — elle ne dit rien de l'élève.
        DisciplinaryMentionPolicy.Suggest(0m, 20, hasGrades: false).Should().BeNull();
    }

    /// <summary>Un bulletin primaire /10 : les seuils sont transposés, Félicitations dès 7/10.</summary>
    [Theory]
    [InlineData(7.0, DisciplinaryMention.Felicitations)]
    [InlineData(6.0, DisciplinaryMention.TableauHonneur)]
    [InlineData(5.99, null)]
    public void Thresholds_Are_Transposed_To_The_Card_Scale(double average, DisciplinaryMention? expected)
    {
        DisciplinaryMentionPolicy.Suggest((decimal)average, 10, hasGrades: true).Should().Be(expected);
    }

    [Fact]
    public void Only_An_Award_Or_Nothing_Is_Ever_Proposed_Never_A_Sanction_Nor_Sans_Distinction()
    {
        // Suggest ne propose qu'une récompense (Encouragements/Tableau d'honneur/Félicitations) ou
        // rien du tout. Ni Blâme/Avertissement (une sanction ne se déduit pas d'un chiffre), ni None
        // (« Sans distinction » est un choix explicite du conseil, jamais une proposition).
        for (var avg = 0m; avg <= 20m; avg += 0.25m)
        {
            var suggestion = DisciplinaryMentionPolicy.Suggest(avg, 20, hasGrades: true);
            suggestion.Should().NotBe(DisciplinaryMention.Blame);
            suggestion.Should().NotBe(DisciplinaryMention.Avertissement);
            suggestion.Should().NotBe(DisciplinaryMention.None);
        }
    }
}
