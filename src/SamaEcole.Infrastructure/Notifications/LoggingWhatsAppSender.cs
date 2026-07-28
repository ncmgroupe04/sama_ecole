using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur d'attente pour l'environnement Development : journalise le message WhatsApp au lieu de l'envoyer.
/// </summary>
public class LoggingWhatsAppSender(ILogger<LoggingWhatsAppSender> logger) : IWhatsAppSender
{
    public Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        var attachmentInfo = message.Attachments is { Count: > 0 }
            ? $"\nPièces jointes : {message.Attachments.Count} fichier(s) joint(s)"
            : string.Empty;

        logger.LogWarning(
            "WhatsApp NON ENVOYÉ (mode développement).\n" +
            "À : {To}\n{Body}{AttachmentInfo}",
            message.To, message.Body, attachmentInfo);

        return Task.CompletedTask;
    }
}
