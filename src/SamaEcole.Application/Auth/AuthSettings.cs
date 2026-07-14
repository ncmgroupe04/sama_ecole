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
}