namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Levée quand l'API WhatsApp Cloud de Meta refuse un envoi DÉCLENCHÉ MANUELLEMENT (envoi d'un
/// bulletin depuis la fiche élève) : rejet 4xx/5xx de graph.facebook.com, jeton absent/expiré,
/// endpoint /media non configuré alors qu'une pièce jointe est requise, ou API injoignable.
///
/// Traduite en HTTP 502 par SamaEcole.Web (ExceptionHandlingMiddleware) — le serveur, passerelle vers
/// Meta, a reçu une réponse invalide de l'amont. Existe pour que « Bulletin envoyé avec succès »
/// cesse de s'afficher quand rien n'est parti : l'expéditeur WhatsApp ne lève jamais de lui-même
/// (canal optionnel, cf. IWhatsAppSender), c'est le handler de l'envoi manuel qui promeut un
/// <see cref="Interfaces.WhatsAppSendStatus.Failed"/> en exception.
///
/// <paramref name="message"/> est déjà rédigé pour l'utilisateur (aucun secret : ni jeton, ni corps
/// de réponse brut de Meta). <paramref name="metaErrorCode"/> et <paramref name="httpStatusCode"/>
/// servent la corrélation dans les journaux, pas l'affichage.
/// </summary>
public class WhatsAppDeliveryException(string message, int? metaErrorCode = null, int? httpStatusCode = null)
    : Exception(message)
{
    public int? MetaErrorCode { get; } = metaErrorCode;

    public int? HttpStatusCode { get; } = httpStatusCode;
}
