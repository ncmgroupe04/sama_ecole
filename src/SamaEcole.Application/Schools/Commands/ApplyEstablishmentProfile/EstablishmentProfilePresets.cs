using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;

/// <summary>
/// PRESET de modules par profil d'Onboarding (docs/superpowers/specs/onboarding-setup-wizard).
/// Ne pilote QUE les 4 commutateurs de <see cref="Domain.Entities.SchoolSettings"/> déjà consommés par
/// ModuleAuthorizationHandler/sidebarNav (Pédagogie, Finance, Internat, Coran) — les nuances plus fines
/// par profil (masquer les Séries, afficher le bloc Bilinguisme…) sont portées par
/// <see cref="ProfileEtablissement"/> lui-même côté front, pas par de nouveaux commutateurs ici.
/// </summary>
public static class EstablishmentProfilePresets
{
    public static ModulePreset For(ProfileEtablissement profile) => profile switch
    {
        // Simplifié : pas de Pédagogie (pas de notes/bulletins), Caisse seule reste active.
        ProfileEtablissement.Simplifie => new ModulePreset(
            IsPedagogyEnabled: false, IsFinanceEnabled: true, IsInternatEnabled: false, IsCoranModuleEnabled: false),

        ProfileEtablissement.ElementairePrimaire => new ModulePreset(
            IsPedagogyEnabled: true, IsFinanceEnabled: true, IsInternatEnabled: false, IsCoranModuleEnabled: false),

        ProfileEtablissement.General => new ModulePreset(
            IsPedagogyEnabled: true, IsFinanceEnabled: true, IsInternatEnabled: false, IsCoranModuleEnabled: false),

        // Franco-Arabe : socle académique + filière Coranique/Franco-Arabe (bilinguisme, matières arabes).
        ProfileEtablissement.FrancoArabe => new ModulePreset(
            IsPedagogyEnabled: true, IsFinanceEnabled: true, IsInternatEnabled: false, IsCoranModuleEnabled: true),

        // Daara/Internat : socle académique + Internat (dortoirs, tuteurs, pension) + Coran (Hifz).
        ProfileEtablissement.DaaraInternat => new ModulePreset(
            IsPedagogyEnabled: true, IsFinanceEnabled: true, IsInternatEnabled: true, IsCoranModuleEnabled: true),

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Profil d'établissement inconnu.")
    };
}

public record ModulePreset(
    bool IsPedagogyEnabled,
    bool IsFinanceEnabled,
    bool IsInternatEnabled,
    bool IsCoranModuleEnabled);
