using System.Net;
using System.Net.Mail;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Ticket JGK-G03 — envoi SMTP réel, utilisé hors Development (voir DependencyInjection.AddInfrastructure,
/// qui bascule sur LoggingEmailSender en Development uniquement).
///
/// System.Net.Mail (BCL) plutôt que MailKit envisagé initialement dans les tickets : le besoin actuel —
/// texte simple, STARTTLS, authentification basique — n'en justifie pas l'ajout comme dépendance externe.
/// À réévaluer si des besoins plus riches apparaissent (pièces jointes, OAuth2, DKIM applicatif…).
/// </summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value;

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(smtp.User, smtp.Password)
        };

        using var mail = new MailMessage(smtp.FromAddress, message.To, message.Subject, message.Body);

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var att in message.Attachments)
            {
                var stream = new MemoryStream(att.Content);
                var attachment = new Attachment(stream, att.Filename, att.ContentType);
                mail.Attachments.Add(attachment);
            }
        }
        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (SmtpException ex)
        {
            // Jamais le corps du message (peut contenir un mot de passe provisoire, Volume_7 §12bis) —
            // seuls le destinataire et l'objet aident au diagnostic sans exposer de secret dans les logs.
            logger.LogError(ex, "Échec d'envoi e-mail à {To} ({Subject}).", message.To, message.Subject);
            throw;
        }
    }
}
