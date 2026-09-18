namespace SamaEcole.Domain.Enums;

/// <summary>
/// Régime d'hébergement d'un élève (module Internat), porté par <see cref="Entities.Enrollment"/> —
/// portée ANNUELLE, comme <see cref="Entities.Enrollment.IsRepeating"/> : une réinscription
/// reconfirme ou change le régime, jamais un report automatique d'une année sur l'autre.
/// </summary>
public enum BoardingStatus
{
    /// <summary>Ne loge pas dans l'établissement. Valeur par défaut.</summary>
    Externe,

    /// <summary>Prend au moins un repas sur place mais ne loge pas la nuit.</summary>
    DemiPensionnaire,

    /// <summary>Loge dans l'établissement — seul régime qui exige une <see cref="Entities.Enrollment.RoomId"/>.</summary>
    Interne
}
