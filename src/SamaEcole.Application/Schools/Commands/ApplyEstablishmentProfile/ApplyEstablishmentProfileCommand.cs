using MediatR;

namespace SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;

/// <summary>
/// POST /schools/current/establishment-profile — Onboarding (assistant de configuration initiale).
/// Réservé au Directeur (comme UpdateSchoolSettingsCommand). Distinct de UpdateSchoolSettingsCommand :
/// choisir un profil applique un PRESET entier de modules d'un coup (voir EstablishmentProfilePresets),
/// ce qu'un simple champ parmi la trentaine de UpdateSchoolSettingsCommand exposerait mal et rendrait
/// facile à dérégler par accident lors d'une future modification de ce formulaire général.
///
/// Aucun SchoolId ici : l'établissement vient du JWT, jamais du corps de la requête (AGENTS.md règle #10).
/// </summary>
public record ApplyEstablishmentProfileCommand(string Profile) : IRequest<SchoolSettingsDto>;
