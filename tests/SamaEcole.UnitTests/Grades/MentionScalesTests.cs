using FluentAssertions;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Transposition des seuils de mention vers le barème d'un bulletin.
///
/// Régression couverte : le bulletin du Primaire (moyennes /10) recevait les seuils /20 tels quels,
/// ce qui qualifiait « Passable » (seuil 8/20) un élève à 9/10 et ne qualifiait pas du tout un élève
/// à 7,5/10 — pourtant l'équivalent de 15/20. Voir MentionScales.
/// </summary>
public class MentionScalesTests
{
    private static readonly IReadOnlyList<(string Label, decimal MinAverage)> DefaultsOn20 =
        MentionDefaults.ForScale(MentionScales.Reference);

    [Fact]
    public void The_Reference_Scale_Is_Twenty()
    {
        MentionScales.Reference.Should().Be(20);
    }

    [Fact]
    public void Rescaling_To_The_Reference_Returns_The_Thresholds_Untouched()
    {
        var rescaled = MentionScales.RescaleTo(DefaultsOn20, 20);

        rescaled.Should().BeEquivalentTo(DefaultsOn20, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Rescaling_To_Ten_Halves_Every_Threshold()
    {
        var rescaled = MentionScales.RescaleTo(DefaultsOn20, 10);

        rescaled.Should().BeEquivalentTo(
            new[]
            {
                ("Excellent", 8m),
                ("Très Bien", 7m),
                ("Bien", 6m),
                ("Assez Bien", 5m),
                ("Passable", 4m)
            },
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void Rescaling_Preserves_The_Descending_Order_Expected_By_MentionFor()
    {
        var rescaled = MentionScales.RescaleTo(DefaultsOn20, 10);

        rescaled.Select(m => m.MinAverage).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Custom_Thresholds_Are_Transposed_Too_Not_Just_The_Defaults()
    {
        // Une école qui a personnalisé ses mentions sur /20 doit voir SON intention transposée, pas
        // les valeurs par défaut réintroduites.
        (string, decimal)[] custom = [("Félicitations", 18m), ("Encouragements", 13m)];

        var rescaled = MentionScales.RescaleTo(custom, 10);

        rescaled.Should().BeEquivalentTo(
            new[] { ("Félicitations", 9m), ("Encouragements", 6.5m) },
            o => o.WithStrictOrdering());
    }

    /// <summary>
    /// Le cas concret du bug : sur /10, un 9,0 est « Excellent » et un 7,5 est « Très Bien » — là où
    /// les seuils /20 non transposés donnaient « Passable » et rien du tout.
    /// </summary>
    [Theory]
    [InlineData(9.0, "Excellent")]
    [InlineData(7.5, "Très Bien")]
    [InlineData(6.2, "Bien")]
    [InlineData(5.0, "Assez Bien")]
    [InlineData(4.0, "Passable")]
    [InlineData(3.9, null)]
    public void A_Primary_Average_Is_Qualified_On_Its_Own_Scale(double average, string? expected)
    {
        var rescaled = MentionScales.RescaleTo(DefaultsOn20, 10);

        SamaEcole.Application.Grades.GradeCalculator
            .MentionFor((decimal)average, rescaled)
            .Should().Be(expected);
    }
}
