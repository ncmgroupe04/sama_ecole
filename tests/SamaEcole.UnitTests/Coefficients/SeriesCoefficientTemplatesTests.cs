using FluentAssertions;
using SamaEcole.Application.Coefficients;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

/// <summary>
/// Modèles nationaux de coefficients par série (Évolution N°4, arbitrage A10 ; référentiel de l'Office du
/// Baccalauréat, Évolution N°6). La table ci-dessous est la table de RÉFÉRENCE : la corriger = modifier
/// SeriesCoefficientTemplates ET ce test, rien d'autre. Une matière d'un groupe d'options y figure sous le nom de
/// chacune de ses alternatives (« Latin », « Grec ») avec le coefficient du groupe.
/// </summary>
public class SeriesCoefficientTemplatesTests
{
    private static readonly Dictionary<string, decimal> Lv2 =
        new() { ["Espagnol"] = 0, ["Allemand"] = 0, ["Arabe"] = 0, ["Italien"] = 0 };

    private static Dictionary<string, decimal> With(Dictionary<string, decimal> common, Dictionary<string, decimal> group, decimal c)
    {
        var result = new Dictionary<string, decimal>(common);
        foreach (var name in group.Keys) result[name] = c;
        return result;
    }

    private static readonly Dictionary<string, decimal> L1Literary = With(new()
    {
        ["Français"] = 5, ["Philosophie"] = 5, ["Latin"] = 4, ["Grec"] = 4, ["Anglais"] = 3,
        ["Histoire-Géographie"] = 3, ["Mathématiques"] = 1, ["EPS"] = 1
    }, Lv2, 2);

    private static readonly Dictionary<string, decimal> Agronomy = new()
    {
        ["Biologie / Agronomie"] = 6, ["Physique-Chimie"] = 5, ["Mathématiques"] = 4, ["Français"] = 3,
        ["Philosophie"] = 2, ["Anglais"] = 2, ["Histoire-Géographie"] = 2, ["EPS"] = 1
    };

    private static readonly Dictionary<string, decimal> Technology = new()
    {
        ["Matières technologiques"] = 8, ["Mathématiques"] = 5, ["Physique-Chimie"] = 5, ["Français"] = 3,
        ["Philosophie"] = 2, ["Anglais"] = 2, ["EPS"] = 1
    };

    private static Dictionary<string, decimal> FrancoArabicScience(string main, decimal c) => new()
    {
        [main] = c, ["Physique-Chimie"] = 6, ["Arabe"] = 4, ["Français"] = 3, ["Philosophie"] = 2, ["Théologie"] = 2,
        ["Histoire-Géographie"] = 2, ["EPS"] = 1
    };

    private static readonly Dictionary<string, Dictionary<string, decimal>> Reference = new()
    {
        ["L1A"] = L1Literary,
        ["L1B"] = L1Literary,
        ["L'1"] = With(new()
        {
            ["Français"] = 5, ["Philosophie"] = 5, ["Anglais"] = 4, ["Histoire-Géographie"] = 3, ["LV3 / Option"] = 2,
            ["Mathématiques"] = 1, ["EPS"] = 1
        }, Lv2, 4),
        ["L2"] = With(new()
        {
            ["Français"] = 5, ["Philosophie"] = 5, ["Histoire-Géographie"] = 5, ["Anglais"] = 3, ["Mathématiques"] = 2,
            ["SVT"] = 2, ["Physique-Chimie"] = 2, ["EPS"] = 1
        }, Lv2, 2),
        ["S1"] = new()
        {
            ["Mathématiques"] = 8, ["Physique-Chimie"] = 8, ["Français"] = 3, ["Philosophie"] = 2, ["SVT"] = 2,
            ["Anglais"] = 2, ["Histoire-Géographie"] = 2, ["EPS"] = 1
        },
        ["S2"] = new()
        {
            ["SVT"] = 6, ["Physique-Chimie"] = 5, ["Mathématiques"] = 5, ["Français"] = 3, ["Philosophie"] = 2,
            ["Anglais"] = 2, ["Histoire-Géographie"] = 2, ["EPS"] = 1
        },
        ["S3"] = new()
        {
            ["Mathématiques"] = 6, ["Physique-Chimie"] = 6, ["Construction / Dessin"] = 5, ["Français"] = 3,
            ["Philosophie"] = 2, ["Anglais"] = 2, ["Histoire-Géographie"] = 2, ["EPS"] = 1
        },
        ["S4"] = Agronomy,
        ["S5"] = Agronomy,
        ["STEG"] = new()
        {
            ["Comptabilité et Gestion"] = 6, ["Économie et Organisation"] = 4, ["Mathématiques appliquées"] = 4,
            ["Droit"] = 3, ["Français"] = 3, ["Anglais"] = 3, ["Philosophie"] = 2, ["Histoire-Géographie"] = 2, ["EPS"] = 1
        },
        ["T1"] = Technology,
        ["T2"] = Technology,
        ["STIDD"] = Technology,
        ["LA"] = new()
        {
            ["Arabe"] = 6, ["Théologie"] = 4, ["Français"] = 4, ["Philosophie"] = 4, ["Histoire-Géographie"] = 3,
            ["Anglais"] = 2, ["Espagnol"] = 2, ["Allemand"] = 2, ["Italien"] = 2, ["Mathématiques"] = 1, ["EPS"] = 1
        },
        ["S1A"] = FrancoArabicScience("Mathématiques", 8),
        ["S2A"] = FrancoArabicScience("SVT", 7),
        // Ancienne nomenclature : le modèle validé le 24/09/2026, inchangé.
        ["L1"] = new()
        {
            ["Français"] = 5, ["Philosophie"] = 5, ["LV2"] = 4, ["Histoire-Géographie"] = 3, ["Anglais"] = 3,
            ["Mathématiques"] = 1, ["Physique-Chimie"] = 1, ["SVT"] = 1, ["EPS"] = 1
        }
    };

    public static TheoryData<string> SeriesWithTemplate => new(Reference.Keys);

    [Theory]
    [MemberData(nameof(SeriesWithTemplate))]
    public void Each_Series_Matches_The_Reference_Table(string series)
        => SeriesCoefficientTemplates.For(series).ToDictionary(l => l.Label, l => l.Coefficient)
            .Should().BeEquivalentTo(Reference[series]);

    [Fact]
    public void Every_Series_Of_The_Catalogue_Except_The_Legacy_Tech_Code_Has_A_Template()
        => LyceeSeries.All.Where(s => SeriesCoefficientTemplates.For(s.Code).Count == 0).Select(s => s.Code)
            .Should().Equal("TECH");

    [Fact]
    public void The_Technical_Legacy_Code_Has_No_National_Template()
        => SeriesCoefficientTemplates.For("TECH").Should().BeEmpty("aucune valeur nationale n'a été fournie pour TECH");

    [Fact]
    public void An_Unknown_Series_Has_No_Template()
        => SeriesCoefficientTemplates.For("S9").Should().BeEmpty();

    [Theory]
    [MemberData(nameof(SeriesWithTemplate))]
    public void A_Template_Is_Well_Formed(string series)
    {
        var lines = SeriesCoefficientTemplates.For(series);

        lines.Should().OnlyContain(l => l.Coefficient > 0 && l.Coefficient <= 20);

        // Aucun alias ne peut désigner deux matières d'une même série, sinon la correspondance serait ambiguë.
        var aliases = lines.SelectMany(l => l.Aliases.Append(l.Label)).Select(SeriesCoefficientTemplates.NormalizeName).ToList();
        aliases.Should().OnlyHaveUniqueItems();

        // Un groupe d'options propose au moins deux alternatives, toutes au même coefficient : le total des
        // coefficients d'un élève ne dépend pas de l'option qu'il choisit.
        foreach (var group in lines.Where(l => l.OptionGroup is not null).GroupBy(l => l.OptionGroup))
        {
            group.Should().HaveCountGreaterThan(1, $"le groupe « {group.Key} » de la série {series} doit offrir un choix");
            group.Select(l => l.Coefficient).Distinct().Should().ContainSingle();
        }
    }

    [Theory]
    [InlineData("L2", "SVT", SeriesCoefficientTemplates.ScienceOptionGroup)]
    [InlineData("L2", "Physique-Chimie", SeriesCoefficientTemplates.ScienceOptionGroup)]
    [InlineData("L2", "Espagnol", SeriesCoefficientTemplates.SecondLanguageGroup)]
    [InlineData("L1A", "Latin", SeriesCoefficientTemplates.AncientLanguageGroup)]
    [InlineData("S1A", "Théologie", SeriesCoefficientTemplates.PhilosophyOrTheologyGroup)]
    [InlineData("S2", "Physique-Chimie", null)]
    [InlineData("LA", "Arabe", null)]
    public void Option_Groups_Follow_The_Reference(string series, string label, string? expectedGroup)
        => SeriesCoefficientTemplates.For(series).Single(l => l.Label == label).OptionGroup.Should().Be(expectedGroup);

    [Fact]
    public void In_The_Arabic_Series_Arabic_Is_Not_Offered_Again_As_A_Second_Language()
        => SeriesCoefficientTemplates.For("LA").Where(l => l.OptionGroup is not null).Select(l => l.Label)
            .Should().NotContain("Arabe");

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
    [InlineData("LV1", "Anglais")]
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
