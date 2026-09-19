using FluentAssertions;
using SamaEcole.Application.Schools.Commands.LockProduction;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Le verrouillage définitif n'a AUCUNE commande de retour en arrière : sa garde de confirmation est
/// le seul rempart entre un clic distrait et une décision irréversible. Testée pour elle-même,
/// indépendamment du Handler — ces cas décrivent EXACTEMENT ce que la modale de l'écran Paramètres
/// doit refuser (pendant JavaScript : lockProductionConfirmationMatches).
/// </summary>
public class LockProductionConfirmationTests
{
    private const string SchoolName = "École Les Baobabs";

    [Fact]
    public void The_Exact_Keyword_Should_Unlock_Lock_Production()
        => LockProductionConfirmation.Matches("VERROUILLER", SchoolName).Should().BeTrue();

    [Fact]
    public void Surrounding_Whitespace_Should_Be_Tolerated()
        => LockProductionConfirmation.Matches("  VERROUILLER  ", SchoolName).Should().BeTrue(
            "un copier-coller ramène souvent une espace, ce n'est pas une hésitation");

    [Theory]
    [InlineData("verrouiller")]
    [InlineData("Verrouiller")]
    [InlineData("VERROUILLE")]
    public void A_Keyword_In_The_Wrong_Case_Should_Not_Unlock_Lock_Production(string typed)
        => LockProductionConfirmation.Matches(typed, SchoolName).Should().BeFalse(
            "la casse exacte est ce qui rend le geste délibéré plutôt que machinal");

    [Theory]
    [InlineData("École Les Baobabs")]
    [InlineData("école les baobabs")]
    [InlineData("  École Les Baobabs ")]
    public void The_School_Name_Should_Also_Unlock_Lock_Production(string typed)
        => LockProductionConfirmation.Matches(typed, SchoolName).Should().BeTrue(
            "le Directeur recopie le nom de son école, il n'a pas à en refaire la graphie exacte");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_Confirmation_Should_Never_Unlock_Lock_Production(string? typed)
        => LockProductionConfirmation.Matches(typed, SchoolName).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_School_Name_Should_Never_Turn_An_Empty_Field_Into_A_Confirmation(string? schoolName)
    {
        LockProductionConfirmation.Matches("", schoolName).Should().BeFalse();
        LockProductionConfirmation.Matches("   ", schoolName).Should().BeFalse();
    }

    [Theory]
    [InlineData("PURGER")]
    [InlineData("CONFIRMER")]
    [InlineData("oui")]
    [InlineData("Baobabs")]
    public void Anything_Else_Should_Be_Refused(string typed)
        => LockProductionConfirmation.Matches(typed, SchoolName).Should().BeFalse();
}
