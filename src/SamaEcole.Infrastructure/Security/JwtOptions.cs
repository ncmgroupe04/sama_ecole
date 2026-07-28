namespace SamaEcole.Infrastructure.Security;

/// <summary>Section "Jwt" de la configuration (appsettings.json, .env.example).</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>Jamais en dur : injectée par la configuration/les secrets (AGENTS.md — pas de secret committé).</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access token court (docs/Volume_4_API_Design.md §1.1 : ~15 min).</summary>
    public int AccessTokenMinutes { get; set; } = 15;
}
