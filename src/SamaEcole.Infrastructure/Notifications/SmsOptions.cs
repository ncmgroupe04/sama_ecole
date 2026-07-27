namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Section "Sms" de la configuration (appsettings.json, .env.example). Même contrat que
/// <see cref="SmtpOptions"/> : valeurs vides par défaut, fournies uniquement par l'environnement
/// (jamais committées, AGENTS.md), et "REMPLACER" compte comme non configuré.
///
/// Volontairement générique (URL + clé + expéditeur) plutôt que taillé pour un agrégateur précis :
/// Infobip, Orange SMS API et Twilio exposent tous un POST JSON de cette forme, et une école
/// sénégalaise change de fournisseur bien plus souvent qu'elle ne change de logiciel.
/// </summary>
public class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Nom de l'agrégateur, conservé dans l'historique des envois (ex. « Infobip »).</summary>
    public string Provider { get; set; } = "Infobip";

    /// <summary>URL complète de l'endpoint d'envoi (ex. https://xxxx.api.infobip.com/sms/2/text/advanced).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Nom ou numéro d'expéditeur affiché sur le téléphone du parent.</summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// Schéma d'autorisation de l'en-tête HTTP. Infobip attend « App », Twilio « Basic », Orange
    /// « Bearer » — d'où un réglage plutôt qu'une valeur codée en dur.
    /// </summary>
    public string AuthScheme { get; set; } = "App";

    public bool IsConfigured =>
        IsValueConfigured(BaseUrl) && IsValueConfigured(ApiKey) && IsValueConfigured(SenderId);

    private static bool IsValueConfigured(string value) =>
        !string.IsNullOrWhiteSpace(value) && value != "REMPLACER";
}
