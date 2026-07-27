namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Accès à la file des SMS pour un traitement HORS TENANT. C'est la raison d'être de ce port séparé
/// d'<see cref="IApplicationDbContext"/> : le worker (SmsQueueProcessor) ne s'exécute dans aucune
/// requête authentifiée, il n'a donc ni JWT ni <see cref="ITenantProvider"/>, et
/// <c>app.current_school_id</c> reste vide. Les policies RLS traduisent cela en « aucune ligne
/// visible » — un DbContext ordinaire verrait une file éternellement vide.
///
/// La réponse N'EST PAS de brancher le worker sur le rôle propriétaire, qui désactiverait la RLS de
/// toutes les tables sans le moindre message (AGENTS.md règle #2, RlsGuard refuse ce montage). Elle
/// est la même que pour le Super Admin : des fonctions PostgreSQL SECURITY DEFINER, au périmètre
/// étroit, qui ne touchent QUE la file et le solde, et dont l'exécution est accordée au seul rôle
/// applicatif — voir la migration AddSmsQueue et SchoolProvisioningStore pour le même idiome.
/// </summary>
public interface ISmsQueueStore
{
    /// <summary>
    /// Réclame un lot de messages en attente, TOUTES ÉCOLES CONFONDUES, et pose un bail : chaque
    /// message réclamé voit sa prochaine échéance repoussée d'autant, si bien qu'une seconde instance
    /// de l'application (ou un redémarrage en cours de lot) ne le reprendra pas en parallèle. Un SMS
    /// envoyé deux fois au parent est bien pire qu'un SMS envoyé une minute plus tard.
    /// </summary>
    Task<IReadOnlyList<QueuedSmsMessage>> ClaimPendingAsync(
        int batchSize, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>
    /// Consigne l'issue d'une remise. En cas d'échec, la décision entre NOUVELLE TENTATIVE et abandon
    /// définitif est prise côté SQL, à partir du nombre de tentatives déjà faites : elle doit être
    /// atomique avec le recréditage du solde, qu'un abandon déclenche.
    /// </summary>
    Task SettleAttemptAsync(
        Guid messageId,
        bool isSent,
        string? providerMessageId,
        string? failureReason,
        int maxAttempts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applique un accusé de réception (DLR) de l'agrégateur, retrouvé par sa propre référence
    /// d'envoi. Renvoie <c>false</c> si aucune ligne ne correspond — un agrégateur peut rejouer un
    /// accusé, ou en envoyer un pour un message que nous ne connaissons pas.
    /// </summary>
    Task<bool> ApplyDeliveryReceiptAsync(
        string providerMessageId,
        bool isDelivered,
        string? failureReason,
        CancellationToken cancellationToken);
}

/// <summary>Message réclamé par le worker : le strict nécessaire pour parler au fournisseur.</summary>
public record QueuedSmsMessage(
    Guid Id,
    Guid SchoolId,
    string Recipient,
    string Body,
    int SegmentCount,
    int AttemptCount);
