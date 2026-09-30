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
    public void FrancoArabe_Enables_The_Coran_Module_Without_Internat()
    {
        var preset = EstablishmentProfilePresets.For(ProfileEtablissement.FrancoArabe);

        preset.IsPedagogyEnabled.Should().BeTrue();
        preset.IsFinanceEnabled.Should().BeTrue();
        preset.IsInternatEnabled.Should().BeFalse();
        preset.IsCoranModuleEnabled.Should().BeTrue();
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
