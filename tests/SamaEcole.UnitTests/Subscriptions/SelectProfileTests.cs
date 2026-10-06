using FluentAssertions;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Application.Subscriptions.Commands.SelectProfile;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>
/// Choix du profil et de la tranche à l'Onboarding : validation de la commande et passerelle entre le profil
/// commercial (ProfileType) et le profil d'Onboarding historique (ProfileEtablissement).
/// </summary>
public class SelectProfileTests
{
    private readonly SelectProfileCommandValidator _validator = new();

    [Theory]
    [InlineData(ProfileType.Elementaire, StudentQuotaTier.Tier1_150)]
    [InlineData(ProfileType.EnseignementGeneral, StudentQuotaTier.Tier2_400)]
    [InlineData(ProfileType.InternatDaara, StudentQuotaTier.Tier3_800)]
    public void A_Known_Profile_With_A_Standard_Tier_Is_Valid(ProfileType profile, StudentQuotaTier tier)
    {
        _validator.Validate(new SelectProfileCommand(profile, tier)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_Custom_Tier_Is_Reserved_To_The_Super_Admin()
    {
        var result = _validator.Validate(new SelectProfileCommand(ProfileType.EnseignementGeneral, StudentQuotaTier.Tier4_Custom));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(SelectProfileCommand.Tier));
    }

    [Fact]
    public void Out_Of_Range_Enum_Values_Are_Rejected()
    {
        var result = _validator.Validate(new SelectProfileCommand((ProfileType)99, (StudentQuotaTier)99));

        result.Errors.Select(e => e.PropertyName).Should().Contain([nameof(SelectProfileCommand.Profile), nameof(SelectProfileCommand.Tier)]);
    }

    [Fact]
    public void Every_Profile_Maps_To_A_Distinct_Establishment_Profile()
    {
        var mapped = Enum.GetValues<ProfileType>().Select(ProfileTypeMapping.ToEstablishmentProfile).ToList();

        mapped.Should().OnlyHaveUniqueItems("deux profils commerciaux ne doivent jamais se confondre côté sidebar");
        mapped.Should().BeEquivalentTo(Enum.GetValues<ProfileEtablissement>(), "chaque ancien profil est atteignable");
    }

    [Theory]
    [InlineData(ProfileType.Elementaire, ProfileEtablissement.ElementairePrimaire)]
    [InlineData(ProfileType.FrancoArabe, ProfileEtablissement.FrancoArabe)]
    [InlineData(ProfileType.InternatDaara, ProfileEtablissement.DaaraInternat)]
    [InlineData(ProfileType.EnseignementGeneral, ProfileEtablissement.General)]
    [InlineData(ProfileType.ComptabiliteRapports, ProfileEtablissement.Simplifie)]
    public void The_Mapping_Matches_The_Correspondence_Used_By_The_Backfill_Migration(
        ProfileType profile, ProfileEtablissement expected)
    {
        ProfileTypeMapping.ToEstablishmentProfile(profile).Should().Be(expected);
    }

    [Fact]
    public void Accounting_Profile_Hides_Pedagogy_And_Daara_Profile_Enables_Boarding_And_Quran()
    {
        var accounting = EstablishmentProfilePresets.For(ProfileTypeMapping.ToEstablishmentProfile(ProfileType.ComptabiliteRapports));
        accounting.IsPedagogyEnabled.Should().BeFalse();
        accounting.IsFinanceEnabled.Should().BeTrue();

        var daara = EstablishmentProfilePresets.For(ProfileTypeMapping.ToEstablishmentProfile(ProfileType.InternatDaara));
        (daara.IsInternatEnabled, daara.IsCoranModuleEnabled).Should().Be((true, true));
    }
}
