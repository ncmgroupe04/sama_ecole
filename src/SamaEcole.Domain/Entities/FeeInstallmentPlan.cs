using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Échéancier personnalisé d'UNE inscription (Étape 5 — recouvrement). Tant qu'aucun plan
/// <see cref="FeeInstallmentPlanStatus.Active"/> n'existe pour une inscription, <c>GetStudentBalanceQuery</c>
/// et <c>GetDuesNoticeQuery</c> continuent de synthétiser des échéances mensuelles uniformes à partir
/// d'<see cref="EnrollmentFeeLine"/> (comportement historique, inchangé) — voir
/// <c>InstallmentScheduleCalculator</c>, seul endroit qui choisit entre les deux sources.
///
/// Au plus UN plan actif par inscription (EnrollmentConfiguration porte l'index unique partiel) : un
/// nouvel accord ne modifie jamais un plan existant, il le remplace (l'ancien passe à <c>Cancelled</c>),
/// pour garder la trace de ce qui a été renégocié — même logique que FeeChangeHistory pour le barème.
/// </summary>
public class FeeInstallmentPlan : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    public FeeInstallmentPlanStatus Status { get; set; } = FeeInstallmentPlanStatus.Active;

    /// <summary>Motif libre de l'échéancier négocié (ex. « difficulté financière, accord du 12/07 »).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Vrai si ce plan a été créé par application en masse d'un modèle à toute une classe
    /// (ApplyFeeInstallmentPlanToClassroomCommand) plutôt que saisi élève par élève — traçabilité
    /// seulement, ne change aucun comportement de calcul.
    /// </summary>
    public bool CreatedFromClassroomTemplate { get; set; }

    public ICollection<FeeInstallment> Installments { get; set; } = new List<FeeInstallment>();
}
