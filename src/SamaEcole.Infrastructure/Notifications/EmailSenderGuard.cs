namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Garde de démarrage — même logique que RlsGuard (SamaEcole.Persistence) : refuser de démarrer plutôt
/// que de dégrader silencieusement vers un comportement dangereux.
///
/// Hors Development, l'application ne doit JAMAIS pouvoir retomber sur LoggingEmailSender (qui
/// journalise les e-mails EN CLAIR, y compris les mots de passe provisoires — voir sa propre
/// documentation) : si aucun SMTP n'est configuré, on préfère un échec de démarrage bruyant à une fuite
/// silencieuse de secrets dans les journaux de production.
///
/// Une seule sortie de secours, explicite et jamais implicite : <c>Smtp__AllowUnconfigured=true</c>
/// (voir <see cref="SmtpOptions.AllowUnconfigured"/>). Elle lève l'échec de démarrage SANS lever
/// l'invariant que la garde protège — l'adaptateur retenu alors, <see cref="UnconfiguredEmailSender"/>,
/// ne journalise aucun corps de message et échoue à l'envoi. Ne jamais la conditionner à un nom
/// d'environnement ni à une heuristique de plateforme : c'est une décision d'exploitation, elle doit
/// rester visible dans la configuration du déploiement.
/// </summary>
public static class EmailSenderGuard
{
    public static void EnsureEmailSenderIsConfigured(SmtpOptions options, bool isDevelopment)
    {
        if (isDevelopment || options.IsConfigured || options.AllowUnconfigured)
        {
            return;
        }

        throw new InvalidOperationException(
            "Aucun serveur SMTP configuré (section 'Smtp' — voir .env.example) en dehors de Development : " +
            "l'application refuse de démarrer plutôt que de journaliser des mots de passe provisoires en " +
            "clair (ticket JGK-G03, AGENTS.md règle sur les secrets). Renseignez Smtp__Host, Smtp__User, " +
            "Smtp__Password et Smtp__FromAddress ; ou, pour un déploiement qui doit monter sans e-mail " +
            "(recette, démonstration, première mise en service), posez explicitement " +
            "Smtp__AllowUnconfigured=true — tout envoi échouera alors bruyamment.");
    }

    /// <summary>
    /// Message d'avertissement à émettre au démarrage quand la sortie de secours est active, ou null
    /// s'il n'y a rien à signaler. Même idiome que <see cref="SmsServiceGuard"/> : la description vit
    /// ici, l'émission dans Program.cs (seul endroit où un ILogger existe déjà).
    /// </summary>
    public static string? DescribeDegradedMode(SmtpOptions options, bool isDevelopment)
    {
        if (isDevelopment || options.IsConfigured || !options.AllowUnconfigured)
        {
            return null;
        }

        return "E-MAIL DÉSACTIVÉ — Smtp__AllowUnconfigured=true et aucun serveur SMTP configuré. Aucun " +
               "message ne partira : mot de passe provisoire du Directeur, réinitialisation de mot de " +
               "passe et envoi de bulletins échoueront à l'appel. Configuration attendue : Smtp__Host, " +
               "Smtp__User, Smtp__Password, Smtp__FromAddress (voir .env.example).";
    }
}
