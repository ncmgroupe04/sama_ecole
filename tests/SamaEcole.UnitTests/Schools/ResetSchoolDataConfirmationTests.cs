using FluentAssertions;
using SamaEcole.Application.Schools.Commands.ResetSchoolData;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// La purge est irréversible : sa garde de confirmation est le seul rempart entre un clic distrait et
/// la perte de toutes les données d'un établissement. Elle est donc testée pour elle-même, indépendamment
/// du Handler — ces cas décrivent EXACTEMENT ce que la modale de l'écran Paramètres doit refuser.
/// </summary>
public class ResetSchoolDataConfirmationTests
{
    private const string SchoolName = "École Les Baobabs";

    [Fact]
    public void The_Exact_Keyword_Should_Unlock_The_Purge()
        => ResetSchoolDataConfirmation.Matches("PURGER", SchoolName).Should().BeTrue();

    [Fact]
    public void Surrounding_Whitespace_Should_Be_Tolerated()
        => ResetSchoolDataConfirmation.Matches("  PURGER  ", SchoolName).Should().BeTrue(
            "un copier-coller ramène souvent une espace, ce n'est pas une hésitation");

    [Theory]
    [InlineData("purger")]
    [InlineData("Purger")]
    [InlineData("PURGé")]
    public void A_Keyword_In_The_Wrong_Case_Should_Not_Unlock_The_Purge(string typed)
        => ResetSchoolDataConfirmation.Matches(typed, SchoolName).Should().BeFalse(
            "la casse exacte est ce qui rend le geste délibéré plutôt que machinal");

    [Theory]
    [InlineData("École Les Baobabs")]
    [InlineData("école les baobabs")]
    [InlineData("  École Les Baobabs ")]
    public void The_School_Name_Should_Also_Unlock_The_Purge(string typed)
        => ResetSchoolDataConfirmation.Matches(typed, SchoolName).Should().BeTrue(
            "le Directeur recopie le nom de son école, il n'a pas à en refaire la graphie exacte");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_Confirmation_Should_Never_Unlock_The_Purge(string? typed)
        => ResetSchoolDataConfirmation.Matches(typed, SchoolName).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_School_Name_Should_Never_Turn_An_Empty_Field_Into_A_Confirmation(string? schoolName)
    {
        // Le piège : comparer une saisie vide à un nom vide les rendrait « égaux ». Une école mal
        // renseignée se purgerait alors sur un simple clic, sans que rien n'ait été saisi.
        ResetSchoolDataConfirmation.Matches("", schoolName).Should().BeFalse();
        ResetSchoolDataConfirmation.Matches("   ", schoolName).Should().BeFalse();
    }

    [Theory]
    [InlineData("École Les Baobab")]
    [InlineData("Baobabs")]
    [InlineData("SUPPRIMER")]
    [InlineData("oui")]
    public void Anything_Else_Should_Be_Refused(string typed)
        => ResetSchoolDataConfirmation.Matches(typed, SchoolName).Should().BeFalse();
}
