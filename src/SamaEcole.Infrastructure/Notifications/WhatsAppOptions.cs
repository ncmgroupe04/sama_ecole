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

    /// <summary>
    /// Nom du modèle Meta (« message template ») approuvé pour l'envoi d'un bulletin. Renseigné, il
    /// débloque le contact « à froid » d'un tuteur : Meta n'autorise un texte LIBRE que dans les 24 h
    /// qui suivent un message entrant du tuteur — hors de cette fenêtre, seul un modèle approuvé passe.
    /// Vide (défaut) : l'expéditeur retombe sur le texte libre, qui n'atteindra que les tuteurs encore
    /// dans la fenêtre.
    ///
    /// Le modèle attendu porte un en-tête de type DOCUMENT (le bulletin PDF) et deux paramètres de
    /// corps : {{1}} = nom de l'élève, {{2}} = période. À créer et faire approuver dans Meta Business
    /// Manager, puis à déclarer ici.
    /// </summary>
    public string ReportCardTemplateName { get; set; } = string.Empty;

    /// <summary>
    /// Code langue du modèle ci-dessus (Meta « language.code » : « fr », « fr_SN », « en_US »…).
    /// Doit correspondre EXACTEMENT à la langue sous laquelle le modèle a été approuvé.
    /// </summary>
    public string TemplateLanguageCode { get; set; } = "fr";

    public bool IsConfigured => IsValueConfigured(BaseUrl) && IsValueConfigured(AccessToken);

    /// <summary>Les pièces jointes exigent l'endpoint /media EN PLUS de la configuration d'envoi.</summary>
    public bool IsMediaConfigured => IsConfigured && IsValueConfigured(MediaUrl);

    /// <summary>
    /// Envoi par modèle possible : configuration de base + un nom de modèle. Le contact « à froid »
    /// avec le bulletin en pièce jointe exige EN PLUS <see cref="IsMediaConfigured"/> (le PDF part en
    /// en-tête document du modèle).
    /// </summary>
    public bool IsReportCardTemplateConfigured => IsConfigured && IsValueConfigured(ReportCardTemplateName);

    private static bool IsValueConfigured(string value) =>
        !string.IsNullOrWhiteSpace(value) && value != "REMPLACER";
}
