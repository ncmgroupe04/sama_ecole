namespace SamaEcole.Application.Schools;

/// <summary>
/// État d'un compteur de matricules pour une année scolaire : son type (« Student » / « Teacher »),
/// le millésime, le dernier numéro déjà attribué et le prochain qui sera émis.
///
/// Partagé par la requête de lecture (GetMatriculeSequences) et la commande de réglage
/// (SetMatriculeSequenceStart) — d'où sa place à la racine du dossier Schools, comme SchoolSettingsDto.
/// </summary>
public record MatriculeSequenceInfo(string Kind, int Year, int LastValue, int NextValue);

/// <summary>Les deux compteurs réglables (élèves, enseignants) pour l'année scolaire en cours.</summary>
public record MatriculeSequencesDto(MatriculeSequenceInfo Student, MatriculeSequenceInfo Teacher);
