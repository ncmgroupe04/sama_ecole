using FluentAssertions;
using SamaEcole.Application.SchoolYears.Commands.DeleteSchoolYear;
using Xunit;

namespace SamaEcole.UnitTests.SchoolYears;

/// <summary>
/// Supprimer une année scolaire efface (en mode test) tout un exercice : sa garde de confirmation est
/// testée pour elle-même, indépendamment du Handler — ces cas décrivent EXACTEMENT ce que la modale de
/// l'écran Années scolaires doit refuser, et son pendant JavaScript (school-years.js :
/// deleteConfirmationMatches) doit se comporter à l'identique.
///
/// Différence VOULUE avec ResetSchoolDataConfirmation : pas de mot-clé fixe, mais le LIBELLÉ de
/// l'année visée. Un mot générique se taperait de mémoire, sur la mauvaise ligne du tableau.
/// </summary>
public class SchoolYearDeletionConfirmationTests
{
    private const string Label = "2025-2026";

    [Fact]
    public void The_Exact_Label_Should_Unlock_The_Deletion()
        => SchoolYearDeletionConfirmation.Matches(Label, Label).Should().BeTrue();

    [Theory]
    [InlineData("  2025-2026  ")]
    [InlineData("2025-2026 ")]
    public void Surrounding_Whitespace_Should_Be_Tolerated(string typed)
        => SchoolYearDeletionConfirmation.Matches(typed, Label).Should().BeTrue(
            "le libellé se recopie à la main : une espace finale n'est pas une hésitation");

    [Fact]
    public void The_Case_Should_Be_Tolerated_On_Labels_That_Carry_Letters()
        => SchoolYearDeletionConfirmation.Matches("année a", "Année A").Should().BeTrue(
            "un libellé n'est pas un mot de passe — c'est la RECOPIE qui fait la garde, pas la graphie");

    [Theory]
    [InlineData("2025-2027")]
    [InlineData("20252026")]
    [InlineData("2025")]
    [InlineData("PURGER")]
    public void Anything_But_The_Label_Should_Be_Refused(string typed)
        => SchoolYearDeletionConfirmation.Matches(typed, Label).Should().BeFalse(
            "le mot-clé générique de la Zone de danger ne doit surtout pas ouvrir cette porte-ci");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_Confirmation_Should_Never_Unlock_The_Deletion(string? typed)
        => SchoolYearDeletionConfirmation.Matches(typed, Label).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_Empty_Label_Should_Never_Be_Matched(string? label)
        => SchoolYearDeletionConfirmation.Matches("2025-2026", label).Should().BeFalse(
            "sans cette réserve, une année au libellé vide se supprimerait sur une saisie quelconque");
}
