using FluentAssertions;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Troisième ligne de l'en-tête du bulletin : le préfixe suit le CYCLE DE LA CLASSE, pas un réglage
/// global d'établissement. Un « LYCÉE DE » codé en dur s'imprimait auparavant sur les bulletins de CM2
/// et de 5e du même complexe scolaire.
/// </summary>
public class SchoolHeadingTests
{
    [Theory]
    [InlineData(CycleType.Maternelle, "ÉCOLE MATERNELLE DE")]
    [InlineData(CycleType.Primaire, "ÉCOLE ÉLÉMENTAIRE DE")]
    [InlineData(CycleType.College, "COLLÈGE DE")]
    [InlineData(CycleType.Lycee, "LYCÉE DE")]
    public void PrefixFor_Follows_The_Classroom_Cycle(CycleType cycle, string expected)
    {
        SchoolHeading.PrefixFor(cycle).Should().Be(expected);
    }

    /// <summary>Tout cycle doit porter un libellé propre — un membre ajouté sans libellé retomberait sur « LYCÉE DE ».</summary>
    [Fact]
    public void PrefixFor_Covers_Every_Cycle_Without_Falling_Back_Silently()
    {
        var prefixes = Enum.GetValues<CycleType>().Select(SchoolHeading.PrefixFor).ToList();

        prefixes.Should().OnlyHaveUniqueItems("chaque cycle a son propre en-tête ; un doublon trahit un membre non traité");
    }

    /// <summary>
    /// Le champ Paramètres est libre et son aide invitait à y écrire le nom « imprimé tel quel après
    /// LYCEE DE : » : beaucoup d'écoles y ont saisi le nom COMPLET. Sans nettoyage, le bulletin d'un
    /// élève de 6e afficherait « COLLÈGE DE : LYCÉE DE POPENGUINE ».
    /// </summary>
    [Theory]
    [InlineData("LYCÉE DE POPENGUINE", "POPENGUINE")]
    [InlineData("Lycee de Popenguine", "Popenguine")]        // Insensible à la casse ET aux accents.
    [InlineData("CEM DE NGAPAROU", "NGAPAROU")]
    [InlineData("Collège de Mbour", "Mbour")]
    [InlineData("École Élémentaire de Saly", "Saly")]        // Préfixe en DEUX mots.
    [InlineData("Ecole Primaire du Point E", "Point E")]     // Liaison « du », reste multi-mots.
    [InlineData("LYCÉE D'EXCELLENCE", "EXCELLENCE")]         // Liaison « D' » collée au nom.
    [InlineData("Lycée Popenguine", "Popenguine")]           // Sans liaison du tout.
    [InlineData("  Lycée   de   Popenguine  ", "Popenguine")]
    [InlineData("École Maternelle de Saly", "Saly")]         // Cycle Maternelle (préfixe en deux mots).
    [InlineData("Crèche de Ngaparou", "Ngaparou")]
    public void StripCyclePrefix_Removes_A_Cycle_Prefix_Typed_By_The_School(string saisi, string expected)
    {
        SchoolHeading.StripCyclePrefix(saisi).Should().Be(expected);
    }

    /// <summary>Un nom qui ne commence par aucun mot de cycle passe intact — on ne mutile pas la saisie.</summary>
    [Theory]
    [InlineData("Popenguine", "Popenguine")]
    [InlineData("Sainte-Jeanne d'Arc", "Sainte-Jeanne d'Arc")]
    [InlineData("Les Baobabs", "Les Baobabs")]
    [InlineData("Cemetière", "Cemetière")]                   // « CEM » n'est un préfixe qu'en mot ENTIER.
    public void StripCyclePrefix_Leaves_An_Ordinary_Name_Untouched(string saisi, string expected)
    {
        SchoolHeading.StripCyclePrefix(saisi).Should().Be(expected);
    }

    /// <summary>
    /// Saisie réduite au seul mot de cycle : rien ne subsisterait après le retrait. On rend alors la
    /// valeur d'origine — un en-tête maladroit mais visible, que le Directeur peut corriger, plutôt que
    /// la disparition silencieuse de la seule donnée qu'il ait renseignée.
    /// </summary>
    [Theory]
    [InlineData("Lycée", "Lycée")]
    [InlineData("LYCÉE DE", "LYCÉE DE")]
    [InlineData("École Élémentaire", "École Élémentaire")]
    public void StripCyclePrefix_Never_Strips_A_Name_Down_To_Nothing(string saisi, string expected)
    {
        SchoolHeading.StripCyclePrefix(saisi).Should().Be(expected);
    }

    /// <summary>Champ non renseigné : la ligne du bulletin s'imprime réduite à son préfixe, jamais un nom inventé.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StripCyclePrefix_Returns_Null_When_Nothing_Was_Entered(string? saisi)
    {
        SchoolHeading.StripCyclePrefix(saisi).Should().BeNull();
    }
}
