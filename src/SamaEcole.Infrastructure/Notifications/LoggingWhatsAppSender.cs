using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur bouchon (aucune section « WhatsApp » configurée, ou environnement Development) :
/// journalise le message au lieu de l'envoyer et renvoie <see cref="WhatsAppSendStatus.Simulated"/>.
///
/// Ce statut EXPLICITE existe pour que l'appelant ne confonde plus « journalisé » et « transmis » :
/// l'envoi manuel d'un bulletin l'affiche en clair à l'utilisateur (« Mode Simulation / Log activé »)
/// au lieu du trompeur « Bulletin envoyé avec succès ».
/// </summary>
public class LoggingWhatsAppSender(ILogger<LoggingWhatsAppSender> logger) : IWhatsAppSender
{
    public Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        var attachmentInfo = message.Attachments is { Count: > 0 }
            ? $"\nPièces jointes : {message.Attachments.Count} fichier(s) joint(s)"
            : string.Empty;

        var templateInfo = message.Template is { } template
            ? $"\nModèle : corps [{string.Join(" | ", template.BodyParameters)}]"
              + (template.HeaderDocument is not null ? " + document en en-tête" : string.Empty)
            : string.Empty;

        logger.LogWarning(
            "WhatsApp NON ENVOYÉ (Mode Simulation / Log activé — section 'WhatsApp' non configurée).\n" +
            "À : {To}\n{Body}{TemplateInfo}{AttachmentInfo}",
            message.To, message.Body, templateInfo, attachmentInfo);

        return Task.FromResult(WhatsAppSendResult.Simulated);
    }
}
