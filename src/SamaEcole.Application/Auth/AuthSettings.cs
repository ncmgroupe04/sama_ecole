namespace SamaEcole.Application.Auth;

/// <summary>
/// Paramètres d'authentification (section "Auth" de la configuration, ticket JGK-A04).
/// Enregistré en singleton par SamaEcole.Web — Application ne lit jamais IConfiguration directement.
/// </summary>
public record AuthSettings
{
    /// <summary>Durée de vie du refresh token. Défaut : 14 jours (.env.example / appsettings.json).</summary>
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>Verrouillage après N échecs consécutifs — docs/Volume_7_Security.md §2.</summary>
    public int MaxFailedAttempts { get; init; } = 5;

    /// <summary>Durée du verrouillage, configurable (docs/Volume_7_Security.md §2).</summary>
    public int LockoutMinutes { get; init; } = 15;

    /// <summary>
    /// Validité d'un lien de réinitialisation self-service. 20 minutes : assez pour relever sa boîte et
    /// choisir un mot de passe, assez court pour qu'un lien resté dans une messagerie consultée plus
    /// tard (poste partagé, boîte compromise) ne soit plus exploitable.
    /// </summary>
    public int PasswordResetMinutes { get; init; } = 20;

    /// <summary>
    /// Racine publique de l'application, utilisée pour construire le lien envoyé par e-mail. Doit être
    /// l'URL vue par l'UTILISATEUR (pas l'adresse interne du conteneur) : elle est cliquée depuis un
    /// client de messagerie, hors du réseau applicatif.
    /// </summary>
    public string PublicBaseUrl { get; init; } = "https://localhost:5001";
}