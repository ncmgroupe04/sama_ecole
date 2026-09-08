using FluentAssertions;
using SamaEcole.Application.Common;
using Xunit;

namespace SamaEcole.UnitTests.Common;

/// <summary>
/// <see cref="EmailNormalizer"/> est le point de passage unique qui garantit qu'un e-mail servant
/// d'identifiant de compte est écrit sous une seule forme, quel que soit le chemin de création
/// (inscription self-service, création directe, ajout de personnel). Sans lui, une simple différence
/// de casse ouvrait un second compte (docs/Volume_3_DDS.md §5.2, audit sécurité).
/// </summary>
public class EmailNormalizerTests
{
    [Theory]
    [InlineData("Directeur@Ecole.SN", "directeur@ecole.sn")]
    [InlineData("DIRECTEUR@ECOLE.SN", "directeur@ecole.sn")]
    [InlineData("  directeur@ecole.sn  ", "directeur@ecole.sn")]
    [InlineData("\tDirecteur@Ecole.sn\n", "directeur@ecole.sn")]
    [InlineData("directeur@ecole.sn", "directeur@ecole.sn")]
    public void Normalize_Folds_Case_And_Trims_Edges(string input, string expected)
    {
        EmailNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_Is_Idempotent()
    {
        var once = EmailNormalizer.Normalize("Awa.Ndiaye@Monécole.SN");
        EmailNormalizer.Normalize(once).Should().Be(once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Turns_A_Missing_Value_Into_An_Empty_String(string? input)
    {
        // Refuser un e-mail manquant est le rôle du validateur de format, en amont — pas celui-ci.
        EmailNormalizer.Normalize(input).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Uses_Invariant_Casing()
    {
        // Le « I » turc ne doit pas devenir « ı » : un e-mail est de l'ASCII, jamais soumis aux
        // règles de casse locales (ToLowerInvariant, pas ToLower).
        EmailNormalizer.Normalize("DIRECTRICE@ISTANBUL.SN").Should().Be("directrice@istanbul.sn");
    }
}
