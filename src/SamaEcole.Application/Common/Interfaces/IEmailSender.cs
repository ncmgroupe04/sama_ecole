namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Envoi d'e-mails transactionnels (ticket JGK-B01 : identifiants du Directeur initial).
///
/// Port volontairement minimal. Deux adaptateurs, choisis par environnement (voir
/// SamaEcole.Infrastructure.DependencyInjection.AddInfrastructure) : SmtpEmailSender (réel, hors
/// Development) et LoggingEmailSender (Development uniquement — journalise au lieu d'envoyer, ne pas
/// confondre « message dispatché » et « message délivré »).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public record EmailMessage(string To, string Subject, string Body);
