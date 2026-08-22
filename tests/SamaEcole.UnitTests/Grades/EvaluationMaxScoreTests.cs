using FluentAssertions;
using SamaEcole.Application.Grades;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Barème PAR LIGNE d'évaluation (grilles APC : /10, /16, /24, /40, /60) : sa résolution, la
/// transposition qui rend ces lignes comparables entre elles, et l'appréciation qui en découle.
///
/// L'enjeu de ces trois règles est le même : une école qui déclare « Compétences /60 » et
/// « Ressources /40 » ne demande PAS que la première pèse une fois et demie la seconde dans la
/// moyenne — elle déclare deux barèmes, pas deux poids. Le poids, lui, reste le coefficient.
/// </summary>
public class EvaluationMaxScoreTests
{
    /// <summary>
    /// Le repli est la garantie de non-régression : MaxScore est NULL sur toutes les matières
    /// antérieures aux grilles configurables, et doit alors valoir le barème du cycle — /10 au primaire,
    /// pas /20. Une valeur par défaut fixe à 20 aurait discrètement relevé le plafond de tout le primaire.
    /// </summary>
    [Theory]
    [InlineData(null, 10, 10)]     // Primaire sans barème propre : /10, comme avant l'option.
    [InlineData(null, 20, 20)]     // Secondaire sans barème propre : /20, comme avant l'option.
    [InlineData(40, 10, 40)]       // Barème déclaré : il l'emporte, même au-dessus du cycle.
    [InlineData(16, 20, 16)]       // …et même en dessous.
    [InlineData(0, 20, 20)]        // Zéro en base est absurde (division par zéro plus loin) : traité comme absent.
    [InlineData(-5, 20, 20)]       // Idem pour une valeur négative.
    public void EffectiveMaxScore_Falls_Back_To_The_Cycle_Scale_When_No_Usable_Value_Is_Declared(
        int? declared, int cycleScale, int expected)
    {
        GradeCalculator.EffectiveMaxScore(declared, cycleScale).Should().Be(expected);
    }

    /// <summary>
    /// 45/60 et 18/24 valent tous deux 15/20 : c'est cette transposition qui permet de moyenner des
    /// lignes de barèmes différents sans que la plus « large » ne pèse mécaniquement plus lourd.
    /// </summary>
    [Theory]
    [InlineData(45, 60, 20, 15)]
    [InlineData(18, 24, 20, 15)]
    [InlineData(8, 10, 20, 16)]
    [InlineData(12, 20, 10, 6)]
    public void Rebase_Converts_A_Score_To_Another_Scale(int value, int from, int to, int expected)
    {
        GradeCalculator.Rebase(value, from, to).Should().Be(expected);
    }

    /// <summary>Barème d'origine nul : la note repart telle quelle, jamais une division par zéro.</summary>
    [Fact]
    public void Rebase_Returns_The_Value_Unchanged_Rather_Than_Dividing_By_Zero()
    {
        GradeCalculator.Rebase(12m, fromScale: 0m, toScale: 20m).Should().Be(12m);
    }

    /// <summary>
    /// Une note déjà exprimée sur le barème cible ne bouge pas — la propriété qui garantit qu'aucun
    /// bulletin existant ne change de valeur du seul fait de cette option.
    /// </summary>
    [Theory]
    [InlineData(13.5, 20)]
    [InlineData(7.25, 10)]
    public void Rebase_Is_The_Identity_When_Both_Scales_Match(double value, int scale)
    {
        GradeCalculator.Rebase((decimal)value, scale, scale).Should().Be((decimal)value);
    }

    private static IReadOnlyList<(string Label, decimal MinAverage)> DefaultMentions() =>
        MentionDefaults.ForScale(MentionScales.Reference);

    /// <summary>
    /// L'appréciation se décide sur le POURCENTAGE de réussite : 8/10, 32/40 et 48/60 valent tous 80 %,
    /// donc « Excellent » avec les seuils par défaut. Sans ce passage par le pourcentage, il faudrait une
    /// échelle d'appréciations par valeur de « Sur » — six échelles pour la seule grille du CE1-CE2.
    /// </summary>
    [Theory]
    [InlineData(8, 10, "Excellent")]
    [InlineData(32, 40, "Excellent")]
    [InlineData(48, 60, "Excellent")]
    [InlineData(12, 24, "Assez Bien")]   // 50 %
    [InlineData(6.4, 16, "Passable")]    // 40 %, le seuil le plus bas
    public void AppreciationFor_Judges_A_Line_On_Its_Percentage_Not_Its_Raw_Value(
        double score, int maxScore, string expected)
    {
        GradeCalculator.AppreciationFor((decimal)score, maxScore, DefaultMentions()).Should().Be(expected);
    }

    /// <summary>Rien de saisi : la case du bulletin reste vide, jamais une appréciation inventée.</summary>
    [Fact]
    public void AppreciationFor_Returns_Null_When_Nothing_Is_Graded_Yet()
    {
        GradeCalculator.AppreciationFor(null, 40m, DefaultMentions()).Should().BeNull();
    }

    /// <summary>
    /// Sous le seuil le plus bas (40 %), aucune mention n'est atteinte : la case s'imprime vide plutôt
    /// que de porter une qualification que l'école n'a pas définie.
    /// </summary>
    [Fact]
    public void AppreciationFor_Returns_Null_Below_The_Lowest_Threshold()
    {
        GradeCalculator.AppreciationFor(10m, 60m, DefaultMentions()).Should().BeNull();
    }
}
