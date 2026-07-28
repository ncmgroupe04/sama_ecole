using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur d'attente : journalise l'e-mail au lieu de l'envoyer. Le mot de passe provisoire du
/// Directeur (ticket JGK-B01) y apparaît donc en clair — c'est le seul endroit où il apparaît.
///
/// Réservé à Development, où c'est le moyen pratique de retrouver ce mot de passe faute d'un vrai
/// serveur SMTP local (DependencyInjection.AddInfrastructure n'enregistre CET adaptateur QUE si
/// isDevelopment ; sinon SmtpEmailSender, ou échec de démarrage si le SMTP n'est pas configuré — voir
/// EmailSenderGuard). Ce cloisonnement est structurel, pas une simple convention : ne pas réenregistrer
/// cet adaptateur inconditionnellement.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var attachmentInfo = message.Attachments is { Count: > 0 }
            ? $"\nPièces jointes : {message.Attachments.Count} fichier(s) joint(s)"
            : string.Empty;

        logger.LogWarning(
            "E-mail NON ENVOYÉ (aucun adaptateur SMTP configuré — ticket JGK-G03).\n" +
            "À : {To}\nObjet : {Subject}\n{Body}{AttachmentInfo}",
            message.To, message.Subject, message.Body, attachmentInfo);

        return Task.CompletedTask;
    }
}
