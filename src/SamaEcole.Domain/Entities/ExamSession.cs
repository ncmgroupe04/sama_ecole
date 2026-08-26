using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Campagne d'examen officiel de l'école pour une année scolaire (Volume 1 §22.1, Volume 3 DDS §5.10) :
/// CFEE, BFEM ou BAC, avec série/option pour ces deux derniers (CFEE n'en a pas).
/// </summary>
public class ExamSession : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SchoolYearId { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;

    public ExamType ExamType { get; set; }

    /// <summary>Série/option (S1, S2, L, G...). Toujours nul pour <see cref="Enums.ExamType.CFEE"/>.</summary>
    public string? Series { get; set; }

    /// <summary>Centre par défaut de la session, surchageable dossier par dossier (<see cref="ExamDossier.ExamCenterName"/>).</summary>
    public string? CenterName { get; set; }

    public ExamSessionStatus Status { get; set; } = ExamSessionStatus.EnPreparation;

    /// <summary>
    /// Compteur interne du numéro de table, incrémenté uniquement par
    /// <c>IExamCandidateNumberGenerator</c> dans la transaction d'attribution (AGENTS.md règle #3).
    /// Jamais exposé tel quel dans un DTO — seul le numéro rendu (<see cref="ExamDossier.CandidateNumber"/>)
    /// l'est.
    /// </summary>
    public int NextCandidateSeq { get; set; }
}
