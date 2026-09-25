using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Fiche d'appel : l'appel d'une classe, pour une matière, à une date et sur un créneau horaire donné
/// (ticket JGK-D06, docs/Volume_3_DDS.md « Attendances »).
///
/// Porte l'année scolaire ACTIVE au moment de la saisie (résolue serveur, jamais choisie par le
/// client — même convention que Enrollment et TeacherAssignment). Une seule fiche par
/// (classe, matière, date, créneau) : une seconde saisie de la même clé est refusée en 409, jamais un
/// doublon silencieux (index unique en base). Le détail élève par élève vit dans
/// <see cref="StudentAttendance"/>.
/// </summary>
public class AttendanceSheet : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ClassroomId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid SchoolYearId { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Créneau horaire en texte libre : « Matin », « 08h-10h », « 1re heure »…</summary>
    public required string Period { get; set; }

    /// <summary>Auteur de l'appel — jamais lu depuis la requête, toujours depuis le JWT (traçabilité).</summary>
    public Guid TakenByUserId { get; set; }

    /// <summary>
    /// Cours d'emploi du temps sur lequel l'appel a été fait (Évolution N°5). Null pour un appel « libre »
    /// (demi-journée, texte libre) — tout appel antérieur à l'évolution. Avec un créneau, <see cref="Period"/>
    /// est DÉRIVÉ de ses horaires côté serveur (SlotPeriod.Label), jamais saisi.
    /// </summary>
    public Guid? ScheduleSlotId { get; set; }
}
