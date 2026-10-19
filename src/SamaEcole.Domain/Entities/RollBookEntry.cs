using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Saisie directe du cahier de roulement : le statut d'UN élève pour UNE demi-journée (Franco-Arabe Bloc 5,
/// docs/superpowers/specs/2026-10-14-franco-arabe-cahier-de-roulement-design.md §2.2).
///
/// Elle PRIME sur le statut dérivé des appels par séance : les fiches des enseignants ne sont jamais
/// réécrites, ce sont deux sources distinctes que le cahier fusionne à la lecture. Supprimer la saisie
/// (suppression logique, règle #6) fait revenir la case au statut calculé.
///
/// Les statuts réutilisent <see cref="AttendanceStatus"/> — aucun statut nouveau : présent, retard, absence
/// justifiée, absence non justifiée. L'absence PARTIELLE n'est jamais saisie, c'est un constat dérivé.
/// </summary>
public class RollBookEntry : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SchoolYearId { get; set; }

    public Guid ClassroomId { get; set; }

    public Guid StudentId { get; set; }

    public DateOnly Date { get; set; }

    public HalfDay HalfDay { get; set; }

    public AttendanceStatus Status { get; set; }

    /// <summary>Minutes de retard. Zéro hors statut <see cref="AttendanceStatus.Late"/> (contrainte en base) ; zéro = non précisé.</summary>
    public int LateMinutes { get; set; }

    /// <summary>Motif libre (justificatif d'absence, cause du retard). Facultatif.</summary>
    public string? Reason { get; set; }

    public Guid RecordedByUserId { get; set; }
}
