namespace SamaEcole.Application.Auth;

/// <summary>
/// Paramètres d'authentification (section "Auth" de la configuration, ticket JGK-A04).
/// Enregistré en singleton par SamaEcole.Web — Application ne lit jamais IConfiguration directement.
/// </summary>
public record AuthSettings
{
    /// <summary>Durée de vie du refresh token. Défaut : 14 jours (.env.example / appsettings.json).</summary>
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>
    /// Seuil du PREMIER palier de verrouillage progressif — docs/Volume_7_Security.md §2. La durée
    /// n'est plus configurable ici : 1 minute à ce seuil, puis +1 heure par tranche de 3 échecs
    /// supplémentaires (calculé dans auth_touch_login, migration AddProgressiveLoginLockout).
    /// </summary>
    public int MaxFailedAttempts { get; init; } = 5;

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