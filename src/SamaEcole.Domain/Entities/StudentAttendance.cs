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
}
