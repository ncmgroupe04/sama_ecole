using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur des déploiements qui montent SANS serveur SMTP, sur renonciation explicite
/// (<c>Smtp__AllowUnconfigured=true</c> — voir <see cref="EmailSenderGuard"/>).
///
/// Deux différences essentielles avec <see cref="LoggingEmailSender"/>, qui est et reste réservé à
/// Development :
///
/// 1. Il ne journalise JAMAIS <see cref="EmailMessage.Body"/>. Le corps d'un message peut contenir le
///    mot de passe provisoire du Directeur (JGK-B01) ou un jeton de réinitialisation — les écrire dans
///    un puits de journaux centralisé est exactement ce que la garde de démarrage existe pour empêcher.
///    Seuls destinataire et objet sont tracés, comme le fait déjà SmtpEmailSender sur échec d'envoi.
/// 2. Il ÉCHOUE au lieu de rendre un succès. Un adaptateur silencieux laisserait croire que le compte
///    d'un Directeur a bien été communiqué alors que personne n'a rien reçu ; l'exception remonte à
///    ExceptionHandlingMiddleware, qui la traduit en erreur API normalisée (Volume_4 §0.4).
/// </summary>
public sealed class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogError(
            "E-mail NON ENVOYÉ : aucun serveur SMTP configuré sur ce déploiement " +
            "(Smtp:AllowUnconfigured=true). À : {To} — Objet : {Subject}",
            message.To, message.Subject);

        throw new InvalidOperationException(
            "Envoi d'e-mail impossible : aucun serveur SMTP n'est configuré sur ce déploiement et la " +
            "garde de démarrage a été explicitement levée (Smtp:AllowUnconfigured=true). Renseignez " +
            "Smtp__Host, Smtp__User, Smtp__Password et Smtp__FromAddress puis redéployez — voir .env.example.");
    }
}
