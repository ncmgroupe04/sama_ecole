using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une unité du programme officiel d'une matière pour un NIVEAU (Évolution N°7 — suivi du syllabus) : un chapitre ou
/// un objectif, dans l'ordre du programme. Chargée depuis la trame nationale codée (SyllabusTemplates) ou saisie par
/// l'école ; les enseignants la pointent dans le cahier de texte (ClassJournalEntryUnit), et l'avancement d'une classe
/// est la part des unités pointées au moins une fois.
/// </summary>
public class SyllabusUnit : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>Niveau, dans la nomenclature ClassroomGradeLevels (« Troisième », « CM2 »…).</summary>
    public required string GradeLevel { get; set; }

    /// <summary>Partie du programme (« Activités numériques », « Géométrie »…) ; null pour un programme sans partie.</summary>
    public string? Section { get; set; }

    public required string Title { get; set; }

    /// <summary>Rang dans le programme : l'ordre pédagogique, jamais alphabétique.</summary>
    public int Order { get; set; }

    /// <summary>Volume horaire indicatif prévu par le programme ; null s'il n'est pas précisé.</summary>
    public decimal? PlannedHours { get; set; }
}
