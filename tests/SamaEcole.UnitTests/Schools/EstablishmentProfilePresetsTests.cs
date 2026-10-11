using FluentAssertions;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Preset de modules par profil d'Onboarding — une seule source de vérité, partagée par
/// ApplyEstablishmentProfileCommandHandler. Ces tests figent la matrice attendue : un changement
/// accidentel de preset (ex. Simplifié qui rallumerait la Pédagogie) casserait silencieusement le
/// masquage de la sidebar/formulaires côté front sans qu'aucun autre test ne le détecte.
/// </summary>
public class EstablishmentProfilePresetsTests
{
    [Fact]
    public void Simplifie_Disables_Pedagogy_But_Keeps_Finance()
    {
        var preset = EstablishmentProfilePresets.For(ProfileEtablissement.Simplifie);

        preset.IsPedagogyEnabled.Should().BeFalse();
        preset.IsFinanceEnabled.Should().BeTrue();
        preset.IsInternatEnabled.Should().BeFalse();
        preset.IsCoranModuleEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(ProfileEtablissement.ElementairePrimaire)]
    [InlineData(ProfileEtablissement.General)]
    public void Elementaire_And_General_Use_The_Standard_Academic_Preset(ProfileEtablissement profile)
    {
        var preset = EstablishmentProfilePresets.For(profile);

        preset.IsPedagogyEnabled.Should().BeTrue();
        preset.IsFinanceEnabled.Should().BeTrue();
        preset.IsInternatEnabled.Should().BeFalse();
        preset.IsCoranModuleEnabled.Should().BeFalse();
    }

    [Fact]
    public void FrancoArabe_Enables_Every_Module()
    {
        // Les Halqas et le suivi coranique sont derrière le module Internat : un profil Franco-Arabe qui doit les
        // proposer (avec les notes et bulletins bilingues) active donc les quatre modules.
        var preset = EstablishmentProfilePresets.For(ProfileEtablissement.FrancoArabe);

        preset.IsPedagogyEnabled.Should().BeTrue();
        preset.IsFinanceEnabled.Should().BeTrue();
        preset.IsInternatEnabled.Should().BeTrue();
        preset.IsCoranModuleEnabled.Should().BeTrue();
    }

    [Fact]
    public void DaaraMemorisation_Disables_Pedagogy_And_Keeps_Internat_Coran_And_Finance()
    {
        var preset = EstablishmentProfilePresets.For(ProfileEtablissement.DaaraMemorisation);

        preset.IsPedagogyEnabled.Should().BeFalse("pas de notes, compositions, bulletins ni matières en mémorisation pure");
        preset.IsFinanceEnabled.Should().BeTrue();
        preset.IsInternatEnabled.Should().BeTrue();
        preset.IsCoranModuleEnabled.Should().BeTrue();
    }

    [Fact]
    public void DaaraInternat_Keeps_The_Academic_Core_Unlike_DaaraMemorisation()
    {
        // « Daara Moderne » reste inchangé : c'est ce qui distingue ce profil de la mémorisation pure.
        EstablishmentProfilePresets.For(ProfileEtablissement.DaaraInternat).IsPedagogyEnabled.Should().BeTrue();
    }

    [Fact]
    public void Only_Daara_And_FrancoArabe_Profiles_Expose_The_Halqas()
    {
        // Les Halqas / le suivi coranique suivent l'interrupteur Internat : les profils généralistes ne les ont pas.
        foreach (var profile in Enum.GetValues<ProfileEtablissement>())
        {
            var exposesHalqas = EstablishmentProfilePresets.For(profile).IsInternatEnabled;
            var expected = profile is ProfileEtablissement.DaaraInternat or ProfileEtablissement.DaaraMemorisation
                or ProfileEtablissement.FrancoArabe;
            exposesHalqas.Should().Be(expected, $"profil {profile}");
        }
    }

    [Fact]
    public void DaaraInternat_Enables_Both_Internat_And_Coran()
    {
        var preset = EstablishmentProfilePresets.For(ProfileEtablissement.DaaraInternat);

        preset.IsPedagogyEnabled.Should().BeTrue();
        preset.IsFinanceEnabled.Should().BeTrue();
        preset.IsInternatEnabled.Should().BeTrue();
        preset.IsCoranModuleEnabled.Should().BeTrue();
    }
}
