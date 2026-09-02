using FluentAssertions;
using SamaEcole.Application.Grades;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Barème FIXE des appréciations par matière du bulletin (colonne « Appréciations ») et seuil du
/// Tableau d'Honneur par matière (colonne « T.H »). Distinct de l'échelle de mentions configurable de
/// l'école — voir SubjectAppreciationScale : les confondre imprimait « Passable » là où la référence
/// visuelle porte « Faible », et rien du tout sous le plus bas seuil de l'école.
/// </summary>
public class SubjectAppreciationScaleTests
{
    [Theory]
    [InlineData(18.0, "Très Bien")]
    [InlineData(16.0, "Très Bien")]
    [InlineData(15.5, "Bon Travail")]
    [InlineData(14.0, "Bon Travail")]
    [InlineData(13.5, "Assez Bien")]
    [InlineData(12.0, "Assez Bien")]
    [InlineData(11.0, "Moyen")]
    [InlineData(10.0, "Moyen")]
    [InlineData(9.5625, "Insuffisant")]
    [InlineData(8.0, "Insuffisant")]
    [InlineData(7.75, "Faible")]
    [InlineData(0.0, "Faible")]
    public void A_Secondary_Average_Gets_The_Reference_Vocabulary(double average, string expected)
    {
        SubjectAppreciationScale.For((decimal)average, 20).Should().Be(expected);
    }

    /// <summary>
    /// Le point qui séparait l'ancien comportement du nouveau : toute matière NOTÉE reçoit une
    /// appréciation, même très basse. GradeCalculator.MentionFor rend null sous le plus bas seuil d'une
    /// échelle de mentions ; le barème d'appréciation, lui, a un plancher « Faible » à zéro.
    /// </summary>
    [Fact]
    public void Even_The_Lowest_Positive_Average_Is_Qualified_Never_Blank()
    {
        SubjectAppreciationScale.For(0.25m, 20).Should().Be("Faible");
    }

    /// <summary>Les valeurs exactes de la référence bulletin-reference.png, sur son propre barème /20.</summary>
    [Theory]
    [InlineData(15.5, "Bon Travail")]   // Français
    [InlineData(14.25, "Bon Travail")]  // Anglais
    [InlineData(13.5, "Assez Bien")]    // Histoire/Géo — « A. Bien » sur la référence
    [InlineData(7.75, "Faible")]        // Maths
    [InlineData(9.5625, "Insuffisant")] // Sciences Physiques
    [InlineData(11.0, "Moyen")]         // SVT
    public void The_Reference_Card_Rows_Reproduce_Its_Printed_Appreciations(double average, string expected)
    {
        SubjectAppreciationScale.For((decimal)average, 20).Should().Be(expected);
    }

    [Theory]
    [InlineData(14.0, true)]
    [InlineData(13.99, false)]
    [InlineData(20.0, true)]
    [InlineData(0.0, false)]
    public void Honors_Are_Earned_From_Fourteen_On_A_Twenty_Point_Scale(double average, bool expected)
    {
        SubjectAppreciationScale.QualifiesForHonors((decimal)average, 20).Should().Be(expected);
    }

    /// <summary>
    /// Un bulletin primaire /10 juge ses moyennes sur des seuils /10 : le T.H s'y décroche à 7/10, pas
    /// à un 14 inatteignable. Même transposition que les appréciations.
    /// </summary>
    [Theory]
    [InlineData(7.0, true)]
    [InlineData(6.99, false)]
    public void Honors_Threshold_Is_Transposed_To_The_Card_Scale(double average, bool expected)
    {
        SubjectAppreciationScale.QualifiesForHonors((decimal)average, 10).Should().Be(expected);
    }

    [Fact]
    public void The_Appreciation_Scale_Is_Transposed_To_Ten_For_Primary_Cards()
    {
        // 7,5/10 équivaut à 15/20 → « Bon Travail », pas « Moyen » (ce que donnaient les seuils /20 bruts).
        SubjectAppreciationScale.For(7.5m, 10).Should().Be("Bon Travail");
        SubjectAppreciationScale.For(4.5m, 10).Should().Be("Insuffisant");
    }

    [Fact]
    public void The_Honor_Threshold_Matches_The_Bon_Travail_Threshold()
    {
        // Les deux colonnes ne peuvent pas se contredire : une matière au T.H atteint « Bon Travail ».
        var bonTravailThreshold = SubjectAppreciationScale.ForScale(20)
            .First(m => m.Label == "Bon Travail").MinAverage;

        SubjectAppreciationScale.HonorMinAverage.Should().Be(bonTravailThreshold);
    }
}
