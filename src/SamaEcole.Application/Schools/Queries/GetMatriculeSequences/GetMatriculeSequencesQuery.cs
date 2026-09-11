using MediatR;

namespace SamaEcole.Application.Schools.Queries.GetMatriculeSequences;

/// <summary>
/// GET /api/v1/schools/current/settings/matricule-sequences — état des compteurs de matricules de
/// l'établissement courant pour l'année scolaire en cours : dernier numéro attribué et prochain
/// numéro à venir, pour les élèves et pour les enseignants.
///
/// Alimente l'encart « Prochain numéro » de l'écran Paramètres › Formats & signatures, à côté du
/// réglage du gabarit. Lecture réservée au Directeur, comme l'écriture (SetMatriculeSequenceStart).
///
/// Le DTO (<see cref="MatriculeSequencesDto"/>) vit à la racine du dossier Schools, partagé avec la
/// commande de réglage.
/// </summary>
public record GetMatriculeSequencesQuery : IRequest<MatriculeSequencesDto>;
