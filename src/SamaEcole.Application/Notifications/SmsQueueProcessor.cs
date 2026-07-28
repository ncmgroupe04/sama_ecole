using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Notifications;

/// <summary>
/// Un TOUR de dépilage de la file : réclamer un lot, le remettre au fournisseur, consigner chaque
/// issue. La boucle et son rythme n'appartiennent pas à cette classe (voir SmsQueueHostedService) —
/// séparation délibérée : un tour est ainsi exécutable seul dans un test, sans horloge à attendre ni
/// service hébergé à démarrer.
///
/// NE LÈVE PAS pour l'échec d'un message : chaque envoi est consigné indépendamment, et une adresse
/// invalide au milieu d'un lot ne doit pas empêcher les suivants de partir.
/// </summary>
public interface ISmsQueueProcessor
{
    /// <summary>Renvoie le nombre de messages traités — 0 signifie « file vide », pas « échec ».</summary>
    Task<int> ProcessOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Réglages du dépilage. Vivent dans la couche Application parce que ce sont des arbitrages MÉTIER
/// (combien de fois insister auprès d'un parent, à partir de quand renoncer), pas des détails de
/// transport ; Infrastructure ne fait que les alimenter depuis la configuration.
/// </summary>
public class SmsQueueSettings
{
    /// <summary>
    /// Messages remis par tour. Volontairement modeste : le lot est traité SÉQUENTIELLEMENT, et un
    /// lot trop grand garderait des messages sous bail bien après l'expiration de celui-ci.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Tentatives avant abandon définitif (et recréditage). Trois suffisent : au-delà, l'échec vient
    /// du numéro lui-même — un numéro invalide ne devient pas valide à la dixième tentative.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Durée du bail posé sur un message réclamé. Doit couvrir largement le délai d'expiration HTTP
    /// du fournisseur (15 s) : si le bail expirait pendant l'appel, un autre tour reprendrait le
    /// message et le parent recevrait le SMS en double.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Temps d'attente entre deux tours quand la file est vide.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Permet de couper le dépilage sans toucher au reste. Les tests fonctionnels s'en servent : un
    /// worker qui tourne en tâche de fond ferait passer un message de Pending à Sent au milieu d'une
    /// assertion, et rendrait la suite non déterministe.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

public class SmsQueueProcessor(
    ISmsQueueStore queueStore,
    ISmsService smsService,
    SmsQueueSettings settings,
    ILogger<SmsQueueProcessor> logger) : ISmsQueueProcessor
{
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var claimed = await queueStore.ClaimPendingAsync(settings.BatchSize, settings.Lease, cancellationToken);

        if (claimed.Count == 0)
        {
            return 0;
        }

        logger.LogInformation("File SMS : {Count} message(s) réclamé(s) pour remise.", claimed.Count);

        foreach (var message in claimed)
        {
            // Annulation (arrêt de l'application) : on s'arrête AVANT d'envoyer, jamais entre l'envoi
            // et sa consignation. Les messages non traités gardent leur bail et repartiront d'eux-mêmes
            // à son expiration — c'est exactement le rôle du bail.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await DispatchOneAsync(message, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task DispatchOneAsync(QueuedSmsMessage message, CancellationToken cancellationToken)
    {
        SmsSendResult result;

        try
        {
            result = await smsService.SendAsync(new SmsSendRequest(message.Recipient, message.Body), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ISmsService s'engage à ne pas lever, mais un adaptateur tiers finit toujours par
            // trahir ce contrat un jour. Le filet est ici, et non plus haut : sans lui, une seule
            // exception emporterait le lot entier et bloquerait la file jusqu'au prochain tour.
            logger.LogError(ex, "Remise du SMS {MessageId} interrompue par une erreur inattendue.", message.Id);
            result = new SmsSendResult(false, null, "Erreur interne lors de la remise.", message.SegmentCount);
        }

        // La consignation se fait SANS le jeton d'annulation : une fois le fournisseur appelé, ne pas
        // enregistrer l'issue serait le pire des cas — le message repartirait à l'expiration du bail
        // et le parent recevrait deux fois la même alerte.
        await queueStore.SettleAttemptAsync(
            message.Id,
            result.IsSent,
            result.ProviderMessageId,
            result.FailureReason,
            settings.MaxAttempts,
            CancellationToken.None);

        if (!result.IsSent)
        {
            logger.LogWarning(
                "SMS {MessageId} non remis (tentative {Attempt}/{Max}) : {Reason}",
                message.Id, message.AttemptCount, settings.MaxAttempts, result.FailureReason);
        }
    }
}
