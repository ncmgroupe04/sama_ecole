using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Passerelle entre le profil COMMERCIAL (<see cref="ProfileType"/>, porté par la souscription) et le
/// profil d'Onboarding historique (<see cref="ProfileEtablissement"/>, porté par <c>SchoolSettings</c> et
/// seul consommé par la sidebar tant qu'elle n'est pas rebranchée). Même correspondance que celle de la
/// migration AddTenantSubscriptions, dans l'autre sens.
/// </summary>
public static class ProfileTypeMapping
{
    public static ProfileEtablissement ToEstablishmentProfile(ProfileType profile) => profile switch
    {
        ProfileType.Elementaire => ProfileEtablissement.ElementairePrimaire,
        ProfileType.FrancoArabe => ProfileEtablissement.FrancoArabe,
        ProfileType.InternatDaara => ProfileEtablissement.DaaraInternat,
        ProfileType.EnseignementGeneral => ProfileEtablissement.General,
        ProfileType.ComptabiliteRapports => ProfileEtablissement.Simplifie,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Profil inconnu.")
    };
}
