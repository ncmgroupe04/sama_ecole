namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Envoi de messages WhatsApp — texte simple, pièces jointes (bulletin PDF), et modèles Meta
/// (<see cref="WhatsAppMessage.Template"/>) pour contacter un tuteur HORS de la fenêtre de 24 h.
///
/// <see cref="SendAsync"/> NE LÈVE PAS : un canal sortant indisponible ne doit pas faire échouer
/// l'action métier qui l'a déclenché (saisie d'un appel, clôture d'un trimestre). Le résultat
/// (<see cref="WhatsAppSendResult"/>) DIT ce qui s'est passé — c'est à l'appelant de décider si un
/// <see cref="WhatsAppSendStatus.Failed"/> doit remonter à l'utilisateur (l'envoi manuel d'un
/// bulletin le fait ; la notification d'absence en tâche de fond l'ignore).
/// </summary>
public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken);
}

/// <param name="To">Numéro E.164 du destinataire (avec ou sans « + »).</param>
/// <param name="Body">
/// Texte libre. Meta ne le délivre QUE si le destinataire a écrit au numéro pro dans les dernières
/// 24 h — sinon il faut un <paramref name="Template"/>. Reste le repli quand aucun modèle n'est
/// configuré.
/// </param>
/// <param name="Attachments">Pièces jointes du message texte libre (ignorées en mode modèle).</param>
/// <param name="Template">
/// Contenu métier d'un modèle Meta (nom de l'élève, période, PDF en en-tête). Renseigné, il permet un
/// envoi « à froid » ; l'expéditeur ne l'utilise QUE si un nom de modèle est configuré, sinon il
/// retombe sur <paramref name="Body"/> + <paramref name="Attachments"/>.
/// </param>
public record WhatsAppMessage(
    string To,
    string Body,
    IReadOnlyList<WhatsAppAttachment>? Attachments = null,
    WhatsAppTemplateContent? Template = null);

public record WhatsAppAttachment(string Filename, byte[] Content, string ContentType);

/// <summary>
/// Les seules données MÉTIER d'un envoi par modèle : l'expéditeur y ajoute le nom du modèle et la
/// langue, lus dans sa configuration (la couche Application n'a pas à les connaître).
/// </summary>
/// <param name="BodyParameters">Paramètres positionnels {{1}}, {{2}}… du corps du modèle.</param>
/// <param name="HeaderDocument">Document joint en en-tête du modèle (le bulletin PDF).</param>
public record WhatsAppTemplateContent(
    IReadOnlyList<string> BodyParameters,
    WhatsAppAttachment? HeaderDocument = null);

public enum WhatsAppSendStatus
{
    /// <summary>Meta a accepté le message.</summary>
    Sent,

    /// <summary>
    /// Aucun fournisseur configuré : le message a été JOURNALISÉ, rien n'est parti
    /// (<see cref="SamaEcole.Application.Common.Interfaces.IWhatsAppSender"/> en mode bouchon).
    /// </summary>
    Simulated,

    /// <summary>Rejet de Meta, jeton absent/expiré, /media non configuré, ou API injoignable.</summary>
    Failed,
}

/// <param name="Status">Issue de l'envoi.</param>
/// <param name="FailureReason">
/// Message prêt pour l'utilisateur (aucun secret) quand <see cref="Status"/> vaut
/// <see cref="WhatsAppSendStatus.Failed"/> ; null sinon.
/// </param>
/// <param name="MetaErrorCode">Code <c>error.code</c> renvoyé par graph.facebook.com, si connu.</param>
/// <param name="HttpStatusCode">Statut HTTP de la réponse Meta, si un appel a bien eu lieu.</param>
public sealed record WhatsAppSendResult(
    WhatsAppSendStatus Status,
    string? FailureReason = null,
    int? MetaErrorCode = null,
    int? HttpStatusCode = null)
{
    public bool IsSent => Status == WhatsAppSendStatus.Sent;
    public bool IsSimulated => Status == WhatsAppSendStatus.Simulated;
    public bool IsFailed => Status == WhatsAppSendStatus.Failed;

    public static WhatsAppSendResult Sent { get; } = new(WhatsAppSendStatus.Sent);
    public static WhatsAppSendResult Simulated { get; } = new(WhatsAppSendStatus.Simulated);

    public static WhatsAppSendResult Failed(string reason, int? metaErrorCode = null, int? httpStatusCode = null)
        => new(WhatsAppSendStatus.Failed, reason, metaErrorCode, httpStatusCode);
}
