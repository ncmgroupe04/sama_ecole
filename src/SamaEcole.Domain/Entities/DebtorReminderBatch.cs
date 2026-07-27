using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Lot de relance de débiteurs, généré chaque nuit par DebtorAgingHostedService pour UNE classe
/// (Étape 5 — recouvrement semi-automatique). Toujours créé <see cref="DebtorReminderBatchStatus.Draft"/> :
/// §13.7 du cahier des charges interdit l'envoi de masse automatique, seul un clic humain
/// (SendDebtorReminderBatchCommand) fait effectivement partir les SMS des <see cref="DebtorReminderBatchItem"/>
/// qu'il contient.
/// </summary>
public class DebtorReminderBatch : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ClassroomId { get; set; }

    /// <summary>Seuil de jours de retard appliqué au moment de la génération (SchoolSettings.DebtorReminderThresholdDays).</summary>
    public int ThresholdDays { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public DebtorReminderBatchStatus Status { get; set; } = DebtorReminderBatchStatus.Draft;

    /// <summary>Utilisateur (Directeur/Finance) qui a déclenché l'envoi — jamais lu depuis la requête, toujours depuis le JWT.</summary>
    public Guid? SentByUserId { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public ICollection<DebtorReminderBatchItem> Items { get; set; } = new List<DebtorReminderBatchItem>();
}
