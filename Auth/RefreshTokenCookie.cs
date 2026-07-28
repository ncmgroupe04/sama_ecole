namespace SamaEcole.Web.Auth;

/// <summary>
/// Le refresh token ne transite QUE par ce cookie (docs/Volume_4_API_Design.md §1.1) : il
/// n'apparaît dans aucun corps de réponse.
///
/// Le renvoyer aussi en JSON annulerait toute la protection : une XSS n'aurait qu'à appeler
/// POST /auth/refresh — le navigateur joint le cookie tout seul — et lire le nouveau refresh token
/// dans la réponse. Un cookie HttpOnly n'a de valeur que si le jeton ne sort par aucun autre canal.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "sama_ecole_refresh_token";

    /// <summary>Restreint aux routes d'authentification : aucune autre requête n'a de raison de le porter.</summary>
    private const string AuthPath = "/api/v1/auth";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Set(HttpResponse response, string refreshToken, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, refreshToken, BuildOptions(expiresAt));

    /// <summary>Les options doivent correspondre à celles de l'écriture, sinon le navigateur garde le cookie.</summary>
    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, BuildOptions(expiresAt: null));

    private static CookieOptions BuildOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = AuthPath,
        Expires = expiresAt,
        IsEssential = true,

        // Inconditionnel, y compris en développement : derrière un reverse proxy qui termine TLS,
        // Request.IsHttps vaut false, et un Secure conditionnel se désactiverait donc précisément
        // en production. Le profil de développement écoute déjà en https (launchSettings.json).
        Secure = true
    };
}
