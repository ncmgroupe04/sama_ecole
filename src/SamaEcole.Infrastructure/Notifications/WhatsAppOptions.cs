namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Section « WhatsApp » de la configuration (appsettings.json, .env.example). Même contrat que
/// <see cref="SmsOptions"/> : valeurs vides par défaut, fournies uniquement par l'environnement
/// (jamais committées, AGENTS.md), et « REMPLACER » compte comme non configuré.
///
/// Taillé pour l'API WhatsApp Cloud de Meta (celle qu'utilisent les revendeurs sénégalais), dont
/// Twilio reprend la forme : un POST JSON authentifié par jeton porteur.
/// </summary>
public class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// URL complète d'envoi, identifiant du numéro expéditeur compris — ex.
    /// https://graph.facebook.com/v21.0/{phone-number-id}/messages
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Jeton d'accès permanent, présenté en Bearer.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// URL de dépôt des pièces jointes (endpoint /media). Sans elle, les bulletins PDF ne peuvent pas
    /// être joints : WhatsApp exige que le fichier soit téléversé AVANT le message qui le référence.
    /// </summary>
    public string MediaUrl { get; set; } = string.Empty;

    public bool IsConfigured => IsValueConfigured(BaseUrl) && IsValueConfigured(AccessToken);

    /// <summary>Les pièces jointes exigent l'endpoint /media EN PLUS de la configuration d'envoi.</summary>
    public bool IsMediaConfigured => IsConfigured && IsValueConfigured(MediaUrl);

    private static bool IsValueConfigured(string value) =>
        !string.IsNullOrWhiteSpace(value) && value != "REMPLACER";
}
