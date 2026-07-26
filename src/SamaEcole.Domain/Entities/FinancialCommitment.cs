using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Engagement financier (reconnaissance de dette) souscrit par le tuteur d'un élève pour régulariser
/// un solde impayé selon un échéancier convenu avec l'établissement — trace écrite de l'accord, pas un
/// mécanisme d'encaissement : il ne touche jamais <see cref="Enrollment.AmountPaid"/> (AGENTS.md règle
/// #4, seul un <c>Payment</c> réel le fait).
/// </summary>
public class FinancialCommitment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public string Terms { get; set; } = null!;
}
