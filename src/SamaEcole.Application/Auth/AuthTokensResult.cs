namespace SamaEcole.Application.Auth;

/// <summary>Schéma AuthTokens d'openapi.yaml.</summary>
public record AuthTokensResult(string AccessToken, string RefreshToken, int ExpiresIn);