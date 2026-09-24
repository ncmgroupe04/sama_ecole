using FluentAssertions;
using SamaEcole.Application.Coefficients;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

/// <summary>
/// Modèles nationaux de coefficients par série (Évolution N°4, arbitrage A10). La table de référence
/// ci-dessous est celle VALIDÉE par la direction le 24/09/2026 : la corriger = modifier
/// SeriesCoefficientTemplates ET ce test, rien d'autre.
/// </summary>
public class SeriesCoefficientTemplatesTests
{
    private static readonly Dictionary<string, Dictionary<string, decimal>> Validated = new()
    {
        ["L1"] = new()
        {
            ["Français"] = 5, ["Philosophie"] = 5, ["LV2"] = 4, ["Histoire-Géographie"] = 3, ["Anglais"] = 3,
            ["Mathématiques"] = 1, ["Physique-Chimie"] = 1, ["SVT"] = 1, ["EPS"] = 1
        },
        ["L2"] = new()
        {
            ["Français"] = 4, ["Philosophie"] = 4, ["Histoire-Géographie"] = 3, ["Anglais"] = 3,
            ["Mathématiques"] = 2, ["LV2"] = 2, ["Physique-Chimie"] = 1, ["SVT"] = 1, ["EPS"] = 1
        },
        ["S1"] = new()
        {
            ["Mathématiques"] = 6, ["Physique-Chimie"] = 6, ["Français"] = 2, ["Philosophie"] = 2,
            ["Histoire-Géographie"] = 2, ["Anglais"] = 2, ["SVT"] = 2, ["LV2"] = 1, ["EPS"] = 1
        },
        ["S2"] = new()
        {
            ["Mathématiques"] = 5, ["Physique-Chimie"] = 5, ["SVT"] = 5, ["Français"] = 2, ["Philosophie"] = 2,
            ["Histoire-Géographie"] = 2, ["Anglais"] = 2, ["LV2"] = 1, ["EPS"] = 1
        }
    };

    [Theory]
    [InlineData("L1")]
    [InlineData("L2")]
    [InlineData("S1")]
    [InlineData("S2")]
    public void Each_Series_Matches_The_Table_Validated_By_The_Direction(string series)
    {
        var lines = SeriesCoefficientTemplates.For(series);

        lines.ToDictionary(l => l.Label, l => l.Coefficient).Should().BeEquivalentTo(Validated[series]);
    }

    [Fact]
    public void The_Technical_Series_Has_No_National_Template_Yet()
        => SeriesCoefficientTemplates.For("TECH").Should().BeEmpty("aucune valeur nationale n'a été fournie pour TECH");

    [Fact]
    public void An_Unknown_Series_Has_No_Template()
        => SeriesCoefficientTemplates.For("S9").Should().BeEmpty();

    [Theory]
    [InlineData("L1")]
    [InlineData("L2")]
    [InlineData("S1")]
    [InlineData("S2")]
    public void A_Template_Is_Well_Formed(string series)
    {
        var lines = SeriesCoefficientTemplates.For(series);

        lines.Should().OnlyContain(l => l.Coefficient > 0 && l.Coefficient <= 20);

        // Aucun alias ne peut désigner deux matières d'une même série, sinon la correspondance serait ambiguë.
        var aliases = lines.SelectMany(l => l.Aliases.Append(l.Label)).Select(SeriesCoefficientTemplates.NormalizeName).ToList();
        aliases.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Mathématiques", "Mathématiques")]
    [InlineData("Mathematiques", "Mathématiques")]
    [InlineData("  maths ", "Mathématiques")]
    [InlineData("Hist-Géo", "Histoire-Géographie")]
    [InlineData("HISTOIRE GEOGRAPHIE", "Histoire-Géographie")]
    [InlineData("Physique Chimie", "Physique-Chimie")]
    [InlineData("PC", "Physique-Chimie")]
    [InlineData("Philo", "Philosophie")]
    [InlineData("Sciences de la vie et de la terre", "SVT")]
    [InlineData("Éducation physique et sportive", "EPS")]
    [InlineData("Langue vivante 2", "LV2")]
    [InlineData("Francais", "Français")]
    public void A_Subject_Name_Is_Recognised_Regardless_Of_Case_Accents_And_Punctuation(string name, string expectedLabel)
        => SeriesCoefficientTemplates.For("S2").Single(l => l.Matches(name)).Label.Should().Be(expectedLabel);

    [Theory]
    [InlineData("Dessin")]
    [InlineData("Informatique")]
    [InlineData("")]
    public void An_Unrelated_Subject_Matches_Nothing(string name)
        => SeriesCoefficientTemplates.For("S2").Should().NotContain(l => l.Matches(name));
}
