using MediatR;

namespace SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;

/// <summary>
/// POST /finance/dues-reminder-batches/{id}/dismiss — écarte un lot BROUILLON sans envoyer aucun SMS
/// (Étape 5), ex. « ces familles ont déjà régularisé en espèces, la relance n'a plus lieu d'être ».
/// Jamais supprimé physiquement (règle #6) : le lot reste consultable, à l'état Dismissed.
/// </summary>
public record DismissDebtorReminderBatchCommand(Guid BatchId) : IRequest;
