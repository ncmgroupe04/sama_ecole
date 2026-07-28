namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Interface pour l'envoi de messages WhatsApp.
/// Supporte l'envoi de texte simple et de pièces jointes (ex: Bulletin PDF).
/// </summary>
public interface IWhatsAppSender
{
    Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken);
}

public record WhatsAppMessage(
    string To,
    string Body,
    IReadOnlyList<WhatsAppAttachment>? Attachments = null);

public record WhatsAppAttachment(string Filename, byte[] Content, string ContentType);
