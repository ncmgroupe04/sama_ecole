namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Section "Smtp" de la configuration (appsettings.json, .env.example) — ticket JGK-G03.
///
/// Comme PayDunyaOptions : valeurs vides par défaut, fournies uniquement par l'environnement (jamais
/// committées, AGENTS.md), et "REMPLACER" (le sentinel de .env.example) compte comme non configuré.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Nom d'expéditeur affiché ("Unikol" plutôt que la seule adresse) : un en-tête From nommé est un
    /// des signaux que les filtres anti-spam associent à un expéditeur légitime plutôt qu'automatisé.
    /// N'affecte ni Host/User/Password/FromAddress ni IsConfigured — reste optionnel côté configuration.
    /// </summary>
    public string FromName { get; set; } = "UNIKOL - Sama École";

    /// <summary>
    /// Renonciation EXPLICITE à la garde de démarrage (EmailSenderGuard), pour un déploiement qui doit
    /// pouvoir monter sans serveur SMTP : recette, démonstration, ou première mise en service d'un
    /// hébergement avant que les identifiants SMTP ne soient disponibles.
    ///
    /// Ce n'est PAS un retour à LoggingEmailSender : l'adaptateur retenu dans ce mode
    /// (UnconfiguredEmailSender) ne journalise jamais le corps d'un message — le mot de passe
    /// provisoire du Directeur (JGK-B01) ne peut donc pas fuiter dans les journaux, ce que la garde
    /// existe précisément pour empêcher. Il échoue bruyamment au premier envoi, plutôt que de laisser
    /// croire qu'un e-mail est parti.
    ///
    /// Faux par défaut, et volontairement à poser variable par variable (Smtp__AllowUnconfigured=true) :
    /// aucun environnement ne bascule dans ce mode par accident.
    /// </summary>
    public bool AllowUnconfigured { get; set; }

    /// <summary>
    /// Centralisé ici pour que SmtpEmailSender ET DependencyInjection (choix entre SmtpEmailSender et
    /// LoggingEmailSender, garde de démarrage) partagent EXACTEMENT la même définition de "non configuré"
    /// — même raisonnement que PayDunyaOptions.IsConfigured.
    /// </summary>
    public bool IsConfigured =>
        IsValueConfigured(Host) && IsValueConfigured(User)
        && IsValueConfigured(Password) && IsValueConfigured(FromAddress);

    private static bool IsValueConfigured(string value) =>
        !string.IsNullOrWhiteSpace(value) && value != "REMPLACER";
}
