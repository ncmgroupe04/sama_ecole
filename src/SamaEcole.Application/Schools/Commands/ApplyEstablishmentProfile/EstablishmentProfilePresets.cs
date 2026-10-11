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

        // Franco-Arabe / hybride : TOUS les modules — socle académique (notes et bulletins bilingues) + Halqas et
        // suivi coranique + Internat. Les Halqas, le suivi coranique et les Oustaz sont derrière le module
        // Internat (décision G-D1) : sans lui, un profil Franco-Arabe n'aurait jamais accès aux Halqas.
        ProfileEtablissement.FrancoArabe => new ModulePreset(
            IsPedagogyEnabled: true, IsFinanceEnabled: true, IsInternatEnabled: true, IsCoranModuleEnabled: true),

        // Daara/Internat : socle académique + Internat (dortoirs, tuteurs, pension) + Coran (Hifz).
        ProfileEtablissement.DaaraInternat => new ModulePreset(
            IsPedagogyEnabled: true, IsFinanceEnabled: true, IsInternatEnabled: true, IsCoranModuleEnabled: true),

        // Daara — Mémorisation : mémorisation coranique pure. Pédagogie coupée (donc ni Notes, ni Examens, ni
        // Bulletins, ni Matières/Coefficients, ni Surveillance générale) ; Internat + Coran + Finance conservés.
        ProfileEtablissement.DaaraMemorisation => new ModulePreset(
            IsPedagogyEnabled: false, IsFinanceEnabled: true, IsInternatEnabled: true, IsCoranModuleEnabled: true),

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Profil d'établissement inconnu.")
    };
}

public record ModulePreset(
    bool IsPedagogyEnabled,
    bool IsFinanceEnabled,
    bool IsInternatEnabled,
    bool IsCoranModuleEnabled);
