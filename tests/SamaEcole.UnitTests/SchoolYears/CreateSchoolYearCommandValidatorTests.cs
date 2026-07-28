using FluentAssertions;
using SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;
using Xunit;

namespace SamaEcole.UnitTests.SchoolYears;

public class CreateSchoolYearCommandValidatorTests
{
    private readonly CreateSchoolYearCommandValidator _validator = new();

    /// <summary>Une année scolaire sénégalaise type : rentrée d'octobre, fin en juillet.</summary>
    private static CreateSchoolYearCommand Valid() => new()
    {
        Label = "2026-2027",
        StartDate = new DateOnly(2026, 10, 1),
        EndDate = new DateOnly(2027, 7, 31)
    };

    [Fact]
    public void Valid_School_Year_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-2027")]
    [InlineData("Année 2026/2027")]
    [InlineData("2026")]
    public void Any_Label_Should_Be_Accepted(string label)
    {
        // Le libellé est un texte d'AFFICHAGE, pas une clé de calcul : aucun format n'est imposé.
        _validator.Validate(Valid() with { Label = label }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Label_Should_Fail()
    {
        _validator.Validate(Valid() with { Label = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void End_Date_Before_Start_Date_Should_Fail()
    {
        var command = Valid() with { StartDate = new DateOnly(2027, 7, 31), EndDate = new DateOnly(2026, 10, 1) };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Missing_Dates_Should_Fail()
    {
        // DateOnly n'est pas nullable : un corps de requête sans dates arrive ici avec 0001-01-01, une
        // valeur qui passerait silencieusement sans ce contrôle.
        var command = Valid() with { StartDate = default, EndDate = default };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Year_Spanning_Decades_Should_Fail()
    {
        // La faute de frappe qu'on cherche à intercepter : « 2036 » saisi au lieu de « 2027 ». Sans
        // cette borne, l'année couvrirait dix ans, chevaucherait toutes les suivantes et bloquerait
        // leur création sans que personne ne comprenne pourquoi.
        var command = Valid() with { EndDate = new DateOnly(2036, 7, 31) };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Year_Lasting_A_Few_Days_Should_Fail()
    {
        var command = Valid() with { EndDate = new DateOnly(2026, 10, 3) };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
