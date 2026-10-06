using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Suivi coranique d'un élève, par Hizb : où il en est sur chacun des 60 Hizb. UNE ligne vivante par
/// (élève, Hizb) — c'est un ÉTAT COURANT, pas un journal d'observations (à l'inverse de
/// <see cref="QuranProgress"/>, qui empile une ligne par observation).
///
/// Invariants tenus par des contraintes CHECK en base (StudentHizbStatusConfiguration), pas seulement
/// par la validation applicative : <see cref="HizbNumber"/> de 1 à 60, <see cref="CompletedQuarters"/> de
/// 0 à 4, <see cref="Rating"/> de 1 à 5 quand il est renseigné, et <see cref="State"/> cohérent avec
/// <see cref="CompletedQuarters"/>.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : plusieurs Oustaz peuvent évaluer le même élève ; le jeton est
/// xmin, comme <see cref="QuranProgress"/>.
/// </summary>
public class StudentHizbStatus : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Numéro du Hizb, de 1 à 60.</summary>
    public int HizbNumber { get; set; }

    /// <summary>Quarts (Rob') acquis : 0 = non commencé, 1 = 1/4, 2 = 2/4, 3 = 3/4, 4 = Hizb complet.</summary>
    public int CompletedQuarters { get; set; }

    /// <summary>Cohérent avec <see cref="CompletedQuarters"/> (voir <see cref="HizbMemorizationState"/>).</summary>
    public HizbMemorizationState State { get; set; } = HizbMemorizationState.NotStarted;

    /// <summary>
    /// Date de la dernière évaluation. NULL tant que le Hizb n'a jamais été évalué : un Hizb non commencé
    /// n'a pas de date d'évaluation, et on n'en invente pas une.
    /// </summary>
    public DateTimeOffset? LastEvaluatedAt { get; set; }

    /// <summary>
    /// Qualité de récitation (Tajweed), de 1 à 5. NULL tant que rien n'a été évalué — même raison que
    /// <see cref="LastEvaluatedAt"/> : une note n'a de sens qu'après une évaluation.
    /// </summary>
    public int? Rating { get; set; }
}
