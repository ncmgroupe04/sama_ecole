using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Statut d'un élève sur une fiche d'appel (ticket JGK-D06). Une ligne par (fiche, élève) — l'index
/// unique l'impose : un même élève ne peut pas figurer deux fois sur le même appel.
///
/// <see cref="LateMinutes"/> n'a de sens que pour <see cref="AttendanceStatus.Late"/> : pour tout
/// autre statut il vaut zéro (invariant tenu par le Handler et validé à la saisie).
/// </summary>
public class StudentAttendance : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid AttendanceSheetId { get; set; }
    public Guid StudentId { get; set; }

    public AttendanceStatus Status { get; set; }

    /// <summary>Minutes de retard, strictement positives si <see cref="Status"/> vaut Late, sinon zéro.</summary>
    public int LateMinutes { get; set; }

    /// <summary>
    /// Billet d'entrée (<see cref="LateArrival"/>) rattaché à cette ligne (Évolution N°5) : l'élève est en
    /// retard parce que la Vie Scolaire l'a autorisé à entrer. Null pour toute ligne sans billet.
    /// </summary>
    public Guid? EntryTicketId { get; set; }

    /// <summary>
    /// Statut et minutes de cette ligne AVANT qu'un billet d'entrée ne la modifie (Complément N°5 bis) — pour un
    /// cours MANQUÉ que le billet passe en absence justifiée. C'est ce qui rend l'annulation possible ligne par
    /// ligne, sans table de journal : une ligne ↔ un billet. Null si aucun billet ne l'a touchée.
    /// </summary>
    public AttendanceStatus? PreviousStatus { get; set; }
    public int? PreviousLateMinutes { get; set; }
}
