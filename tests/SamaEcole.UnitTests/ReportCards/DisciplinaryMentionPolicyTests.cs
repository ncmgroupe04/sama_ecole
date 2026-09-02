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
    [InlineData(16.0, DisciplinaryMention.Felicitations)]
    [InlineData(15.99, DisciplinaryMention.TableauHonneur)]
    [InlineData(14.0, DisciplinaryMention.TableauHonneur)]
    [InlineData(13.5, DisciplinaryMention.Encouragements)]
    [InlineData(12.0, DisciplinaryMention.Encouragements)]
    public void An_Award_Is_Proposed_From_Twelve_On(double average, DisciplinaryMention expected)
    {
        DisciplinaryMentionPolicy.Suggest((decimal)average, 20, hasGrades: true).Should().Be(expected);
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

    /// <summary>Un bulletin primaire /10 : les seuils sont transposés, Félicitations dès 8/10.</summary>
    [Theory]
    [InlineData(8.0, DisciplinaryMention.Felicitations)]
    [InlineData(7.0, DisciplinaryMention.TableauHonneur)]
    [InlineData(6.0, DisciplinaryMention.Encouragements)]
    [InlineData(5.99, null)]
    public void Thresholds_Are_Transposed_To_The_Card_Scale(double average, DisciplinaryMention? expected)
    {
        DisciplinaryMentionPolicy.Suggest((decimal)average, 10, hasGrades: true).Should().Be(expected);
    }

    [Fact]
    public void Blame_And_Avertissement_Are_Never_Returned_At_Any_Average()
    {
        for (var avg = 0m; avg <= 20m; avg += 0.25m)
        {
            var suggestion = DisciplinaryMentionPolicy.Suggest(avg, 20, hasGrades: true);
            suggestion.Should().NotBe(DisciplinaryMention.Blame);
            suggestion.Should().NotBe(DisciplinaryMention.Avertissement);
        }
    }
}
