namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Décode et AUTHENTIFIE un accusé de réception (DLR) d'agrégateur SMS. Même partage des rôles que
/// pour le webhook de paiement (<see cref="IPaymentService"/>) : la couche Application ne sait pas
/// lire un corps propriétaire ni vérifier une signature, l'adaptateur ne connaît aucune règle métier.
///
/// Ce qui est décrit ici n'est PAS un simple parseur : la vérification de signature en fait partie,
/// et elle est indissociable du décodage. Un webhook est une route publique — sans elle, n'importe
/// qui pourrait déclarer « livré » un SMS jamais parti, ou « échoué » un SMS livré et faire ainsi
/// recréditer des segments à volonté.
/// </summary>
public interface ISmsDeliveryReceiptReader
{
    /// <summary>Nom de l'agrégateur reconnu par cet adaptateur, tel qu'il apparaît dans l'URL du webhook.</summary>
    string ProviderName { get; }

    /// <summary>
    /// <paramref name="signatureHeader"/> est l'en-tête de signature brut, tel que reçu. Renvoie
    /// <c>false</c> si la signature est absente, invalide, ou si aucun secret n'est configuré —
    /// jamais d'exception, c'est au Handler d'en faire un 401 (docs/Volume_7_Security.md §12bis).
    /// </summary>
    bool TryRead(
        string rawBody,
        string? signatureHeader,
        out IReadOnlyList<SmsDeliveryReceipt> receipts);
}

/// <summary>
/// Un accusé pour UN message, identifié par la référence rendue par l'agrégateur à l'envoi
/// (SmsMessage.ProviderMessageId).
/// </summary>
public record SmsDeliveryReceipt(string ProviderMessageId, bool IsDelivered, string? FailureReason);
