namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Ticket JGK-I05 — abstraction d'un agrégateur de paiement (PayDunya, CinetPay…), docs/Volume_1_Cahier_des_Charges.md
/// §11.6 : « intégration via un agrégateur… le choix définitif reste à valider ». Application ne connaît
/// QUE ce contrat ; l'implémentation réelle (appel HTTP, clés API, format de facture propre à
/// l'agrégateur) vit dans SamaEcole.Infrastructure — remplacer PayDunya par un autre agrégateur ne
/// touchera jamais cette interface ni les Handlers qui la consomment.
/// </summary>
public interface IPaymentService
{
    /// <summary>Nom de l'agrégateur (ex. "PayDunya"), stocké tel quel sur SubscriptionPayment.Provider.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Crée la facture/session de paiement chez l'agrégateur et renvoie l'URL de son guichet sécurisé.
    /// Lève <see cref="Exceptions.PaymentProviderException"/> si l'agrégateur refuse ou est injoignable —
    /// jamais de résultat "à moitié" : soit une facture valide, soit une erreur explicite.
    /// </summary>
    Task<PaymentInitiationResult> InitiatePaymentAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Ticket JGK-I06 — décode le corps brut d'un webhook entrant et VÉRIFIE sa signature, AVANT tout
    /// autre traitement (docs/Volume_7_Security.md §12bis). Ne lève jamais : une signature absente ou un
    /// corps illisible se traduisent par <see cref="WebhookParseResult.SignatureValid"/> = false, c'est
    /// au Handler de décider d'en faire une InvalidWebhookSignatureException (401).
    /// </summary>
    WebhookParseResult ParseWebhook(string rawBody, string? contentType);

    /// <summary>
    /// Ticket JGK-I06 — vérification IPN serveur à serveur : interroge l'agrégateur DIRECTEMENT (avec NOS
    /// propres clés, jamais avec une donnée venue du webhook) pour connaître le statut RÉEL de la facture
    /// et le montant RÉELLEMENT payé. C'est cet appel, pas le corps du webhook, qui fait foi
    /// (docs/Volume_7_Security.md §12bis : « Aucune confiance dans le client pour l'état d'un paiement »).
    /// Lève <see cref="Exceptions.PaymentProviderException"/> si l'agrégateur est injoignable — dans ce
    /// cas, ne RIEN confirmer ; le webhook sera rejoué par l'agrégateur.
    /// </summary>
    Task<PaymentConfirmationResult> ConfirmPaymentAsync(string providerTransactionRef, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="InternalPaymentId"/> est notre PROPRE identifiant (SubscriptionPayment.Id), transmis
/// à l'agrégateur en donnée personnalisée lors de l'initiation (voir PaymentInitiationRequest) et relu
/// ici : c'est le canal de corrélation le plus fiable, puisque nous en maîtrisons entièrement la forme
/// — contrairement au reste du corps du webhook, dont la forme exacte dépend de l'agrégateur.
/// <paramref name="NormalizedPayloadJson"/> est stocké tel quel dans SubscriptionPayment.WebhookPayloadRaw
/// (audit/réconciliation), que le corps d'origine ait été form-urlencoded ou JSON.
/// </summary>
public record WebhookParseResult(bool SignatureValid, Guid? InternalPaymentId, string NormalizedPayloadJson);

/// <summary><paramref name="RawStatus"/> est la valeur brute renvoyée par l'agrégateur, conservée pour le diagnostic.</summary>
public record PaymentConfirmationResult(bool IsPaid, decimal Amount, string RawStatus);

/// <summary>
/// <paramref name="InternalPaymentId"/> est transmis à l'agrégateur en donnée personnalisée (custom
/// data) : c'est ce qui permettra au futur traitement du webhook (JGK-I06) de retrouver la ligne
/// SubscriptionPayment correspondante même par un canal indépendant de ProviderTransactionRef.
/// </summary>
public record PaymentInitiationRequest(
    Guid InternalPaymentId,
    Guid SchoolId,
    decimal Amount,
    string Currency,
    string Description,
    string CustomerName,
    string CustomerEmail);

/// <summary><paramref name="ProviderTransactionRef"/> sert à la déduplication du futur webhook (unique en base).</summary>
public record PaymentInitiationResult(string ProviderTransactionRef, string RedirectUrl);
