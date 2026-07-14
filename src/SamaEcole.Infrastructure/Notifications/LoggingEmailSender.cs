using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur d'attente : journalise l'e-mail au lieu de l'envoyer.
///
/// ⚠️ NE DÉLIVRE RIEN. L'adaptateur SMTP réel (MailKit, section Smtp de la configuration) relève du
/// ticket JGK-G03, hors du périmètre MVP. Tant qu'il n'existe pas, le Directeur créé par JGK-B01 ne
/// reçoit PAS son e-mail : son mot de passe provisoire est à récupérer dans les journaux du serveur.
///
/// C'est acceptable en développement et pour le premier client piloté à la main ; ce ne l'est pas
/// en exploitation ouverte. Le mot de passe est volontairement journalisé — c'est le seul endroit
/// où il apparaît — ce qui est aussi la raison pour laquelle cet adaptateur ne doit pas survivre à
/// la mise en production réelle.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "E-mail NON ENVOYÉ (aucun adaptateur SMTP configuré — ticket JGK-G03).\n" +
            "À : {To}\nObjet : {Subject}\n{Body}",
            message.To, message.Subject, message.Body);

        return Task.CompletedTask;
    }
}
