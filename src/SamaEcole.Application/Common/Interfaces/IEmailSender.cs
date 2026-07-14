namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Envoi d'e-mails transactionnels (ticket JGK-B01 : identifiants du Directeur initial).
///
/// Port volontairement minimal : l'adaptateur SMTP réel (MailKit) relève du ticket JGK-G03, hors du
/// périmètre MVP. En attendant, l'adaptateur enregistré journalise le message — voir
/// LoggingEmailSender, et ne pas confondre « message dispatché » et « message délivré ».
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public record EmailMessage(string To, string Subject, string Body);
