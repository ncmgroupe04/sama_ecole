using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Note d'examen oral de récitation coranique (module Coran/Franco-Arabe, docs/superpowers/specs/
/// 2026-09-20-franco-arabic-core-design.md §3.4). <see cref="EvaluationDate"/> remplace le
/// <c>ExamId</c> envisagé initialement : aucune entité "session d'examen Coran" n'existe ni n'est
/// justifiée dans ce lot (décision #4 de la spec).
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : c'est une note, au même titre que <see cref="Grade"/>.
/// Le jeton est xmin, configuré dans QuranEvaluationConfiguration.
/// </summary>
public class QuranEvaluation : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public DateOnly EvaluationDate { get; set; }

    public int MemoryMistakes { get; set; }

    public int TajwidMistakes { get; set; }

    public int Hesitations { get; set; }

    /// <summary>Note finale de l'examen oral, sur le barème que l'école choisit pour cet exercice.</summary>
    public decimal FinalScore { get; set; }
}
