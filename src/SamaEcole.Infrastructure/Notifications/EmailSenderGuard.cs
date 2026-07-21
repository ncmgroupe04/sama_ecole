namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Garde de démarrage — même logique que RlsGuard (SamaEcole.Persistence) : refuser de démarrer plutôt
/// que de dégrader silencieusement vers un comportement dangereux.
///
/// Hors Development, l'application ne doit JAMAIS pouvoir retomber sur LoggingEmailSender (qui
/// journalise les e-mails EN CLAIR, y compris les mots de passe provisoires — voir sa propre
/// documentation) : si aucun SMTP n'est configuré, on préfère un échec de démarrage bruyant à une fuite
/// silencieuse de secrets dans les journaux de production.
/// </summary>
public static class EmailSenderGuard
{
    public static void EnsureEmailSenderIsConfigured(SmtpOptions options, bool isDevelopment)
    {
        if (isDevelopment || options.IsConfigured)
        {
            return;
        }

        throw new InvalidOperationException(
            "Aucun serveur SMTP configuré (section 'Smtp' — voir .env.example) en dehors de Development : " +
            "l'application refuse de démarrer plutôt que de journaliser des mots de passe provisoires en " +
            "clair (ticket JGK-G03, AGENTS.md règle sur les secrets).");
    }
}
