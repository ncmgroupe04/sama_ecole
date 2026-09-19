using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Cahier de texte / journal de classe (ticket JGK-P04, docs/BACKLOG_TICKETS.md §Module P) : une
/// entrée par séance réellement assurée, pour une classe et une matière données, saisie par
/// l'enseignant titulaire du créneau (vérifié à l'écriture contre <see cref="TeacherAssignment"/>
/// et <see cref="ScheduleSlot"/>, voir ClassJournalScopeAuthorizer — jamais en base ici).
///
/// <see cref="TeacherId"/> est l'AUTEUR de l'entrée, jamais lu depuis la requête (traçabilité,
/// même convention que <see cref="AttendanceSheet.TakenByUserId"/>). Librement modifiable par son
/// auteur pendant 15 jours après <see cref="SessionDate"/> ; passé ce délai, seuls Directeur et
/// Secrétariat peuvent corriger (la correction est alors historisée via IAuditableRequest sur
/// UpdateClassJournalEntryCommand).
/// </summary>
public class ClassJournalEntry : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ClassroomId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TeacherId { get; set; }

    /// <summary>Date de la séance RÉELLEMENT tenue — jamais future (on journalise ce qui a été fait).</summary>
    public DateOnly SessionDate { get; set; }

    public required string Topic { get; set; }
    public required string Content { get; set; }

    /// <summary>Devoirs donnés à l'issue de la séance — facultatif, toute séance n'en donne pas.</summary>
    public string? Homework { get; set; }

    /// <summary>Date de rendu des devoirs — n'a de sens que si <see cref="Homework"/> est renseigné.</summary>
    public DateOnly? HomeworkDueDate { get; set; }
}
