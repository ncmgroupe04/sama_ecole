namespace SamaEcole.Web.Contracts;

/// <summary>
/// Schéma AuthTokens d'openapi.yaml, tel qu'il sort sur le réseau.
///
/// Se distingue volontairement d'AuthTokensResult (couche Application, qui porte les deux jetons) :
/// le refresh token n'est jamais sérialisé ici, il part uniquement dans le cookie HttpOnly posé par
/// <see cref="Auth.RefreshTokenCookie"/>.
/// </summary>
public record AuthTokensResponse(string AccessToken, int ExpiresIn);
