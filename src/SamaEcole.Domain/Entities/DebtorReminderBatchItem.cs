using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Un débiteur candidat au sein d'un <see cref="DebtorReminderBatch"/> — instantané pris au moment de
/// la génération du lot (montant dû, retard). <see cref="SmsMessageId"/> n'est renseigné qu'après
/// l'envoi effectif du lot (SendDebtorReminderBatchCommand), et relie cette ligne à la trace
/// SmsMessage réellement mise en file par ISmsDispatcher.
/// </summary>
public class DebtorReminderBatchItem : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid DebtorReminderBatchId { get; set; }

    public Guid EnrollmentId { get; set; }

    public Guid StudentId { get; set; }

    public string? GuardianPhone { get; set; }

    /// <summary>Solde restant dû au moment de la génération du lot, en FCFA.</summary>
    public decimal RemainingBalance { get; set; }

    /// <summary>Ancienneté de la plus ancienne échéance impayée, en jours, au moment de la génération du lot.</summary>
    public int DaysOverdue { get; set; }

    /// <summary>Renseigné uniquement après envoi — trace du SMS réellement mis en file pour ce débiteur.</summary>
    public Guid? SmsMessageId { get; set; }

    public DebtorReminderBatch DebtorReminderBatch { get; set; } = null!;
}
