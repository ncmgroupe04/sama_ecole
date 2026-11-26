namespace SamaEcole.Web.Security;

/// <summary>
/// Configuration de Cloudflare Turnstile (section « Turnstile »), le contrôle anti-robot du formulaire PUBLIC
/// d'inscription. <b>Désactivé tant que les deux clés ne sont pas posées</b> : sans clé, rien ne change (développement,
/// tests, premiers déploiements). Dès qu'elles le sont, le jeton du widget devient OBLIGATOIRE et vérifié côté serveur.
///
/// <c>SiteKey</c> est publique (elle est écrite dans la page). <c>SecretKey</c> ne vit JAMAIS dans le dépôt : variable
/// d'environnement <c>Turnstile__SecretKey</c> (Secret Manager sur Cloud Run) — voir .env.example.
/// </summary>
public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public string? SiteKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Vrai seulement si LES DEUX clés sont renseignées : une demi-configuration ne bloque personne.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
