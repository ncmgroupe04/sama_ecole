using FluentAssertions;
using SamaEcole.Application.Schools.Commands.RevertToTest;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Le retour en mode test est disponible à tout moment : sa garde de confirmation est le seul
/// rempart entre un clic distrait et une bascule qui rejoue « Passer en mode réel ». Testée pour
/// elle-même, indépendamment du Handler — ces cas décrivent EXACTEMENT ce que la modale de l'écran
/// Paramètres doit refuser (pendant JavaScript : revertToTestConfirmationMatches).
/// </summary>
public class RevertToTestConfirmationTests
{
    private const string SchoolName = "École Les Baobabs";

    [Fact]
    public void The_Exact_Keyword_Should_Unlock_The_Revert()
        => RevertToTestConfirmation.Matches("TEST", SchoolName).Should().BeTrue();

    [Fact]
    public void Surrounding_Whitespace_Should_Be_Tolerated()
        => RevertToTestConfirmation.Matches("  TEST  ", SchoolName).Should().BeTrue(
            "un copier-coller ramène souvent une espace, ce n'est pas une hésitation");

    [Theory]
    [InlineData("test")]
    [InlineData("Test")]
    [InlineData("TES")]
    public void A_Keyword_In_The_Wrong_Case_Should_Not_Unlock_The_Revert(string typed)
        => RevertToTestConfirmation.Matches(typed, SchoolName).Should().BeFalse(
            "la casse exacte est ce qui rend le geste délibéré plutôt que machinal");

    [Theory]
    [InlineData("École Les Baobabs")]
    [InlineData("école les baobabs")]
    [InlineData("  École Les Baobabs ")]
    public void The_School_Name_Should_Also_Unlock_The_Revert(string typed)
        => RevertToTestConfirmation.Matches(typed, SchoolName).Should().BeTrue(
            "le Directeur recopie le nom de son école, il n'a pas à en refaire la graphie exacte");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_Confirmation_Should_Never_Unlock_The_Revert(string? typed)
        => RevertToTestConfirmation.Matches(typed, SchoolName).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_School_Name_Should_Never_Turn_An_Empty_Field_Into_A_Confirmation(string? schoolName)
    {
        RevertToTestConfirmation.Matches("", schoolName).Should().BeFalse();
        RevertToTestConfirmation.Matches("   ", schoolName).Should().BeFalse();
    }

    [Theory]
    [InlineData("CONFIRMER")]
    [InlineData("oui")]
    [InlineData("Baobabs")]
    public void Anything_Else_Should_Be_Refused(string typed)
        => RevertToTestConfirmation.Matches(typed, SchoolName).Should().BeFalse();
}
