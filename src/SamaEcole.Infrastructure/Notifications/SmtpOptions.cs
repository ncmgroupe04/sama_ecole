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
