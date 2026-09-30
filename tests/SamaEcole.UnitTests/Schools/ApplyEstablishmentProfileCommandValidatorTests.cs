using FluentAssertions;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

public class ApplyEstablishmentProfileCommandValidatorTests
{
    private readonly ApplyEstablishmentProfileCommandValidator _validator = new();

    [Theory]
    [InlineData("Simplifie")]
    [InlineData("ElementairePrimaire")]
    [InlineData("General")]
    [InlineData("FrancoArabe")]
    [InlineData("DaaraInternat")]
    [InlineData("general")] // casse ignorée, comme TypeEtablissement/SchoolType
    public void Known_Profiles_Are_Accepted(string profile)
        => _validator.Validate(new ApplyEstablishmentProfileCommand(profile)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("Inconnu")]
    [InlineData("Prive")] // TypeEtablissement n'est PAS un profil : les deux enums ne se confondent pas
    [InlineData("Standard")] // SchoolType non plus
    public void Unknown_Profiles_Are_Rejected(string profile)
        => _validator.Validate(new ApplyEstablishmentProfileCommand(profile)).IsValid.Should().BeFalse();
}
