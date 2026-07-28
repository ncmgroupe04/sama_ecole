using MediatR;

namespace SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;

/// <summary>
/// POST /finance/dues-reminder-batches/{id}/send — LE seul geste qui fait effectivement partir les
/// SMS d'un lot BROUILLON (Étape 5). Déclenché à la main par un Directeur/Finance, jamais par le job
/// nocturne lui-même : §13.7 du cahier des charges interdit tout envoi de masse automatique — le
/// calcul (GenerateDebtorReminderBatchesCommand) et l'envoi sont deux actes délibérément séparés.
/// </summary>
public record SendDebtorReminderBatchCommand(Guid BatchId) : IRequest<SendDebtorReminderBatchResult>;

/// <summary>Mêmes conventions de comptage que SendDuesReminderSmsCommand : "mis en file", pas "envoyé".</summary>
public record SendDebtorReminderBatchResult(int QueuedCount, int SkippedCount, string? FirstSkipReason);
