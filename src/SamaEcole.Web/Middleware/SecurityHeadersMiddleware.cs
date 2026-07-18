namespace SamaEcole.Web.Middleware;

/// <summary>
/// Ticket JGK-F01 (stabilisation) — en-têtes de sécurité HTTP sur TOUTES les réponses (pages Razor,
/// API, fichiers statiques, erreurs). Posés dans tous les environnements : ce qui protège la
/// production doit être observable par les tests fonctionnels (qui tournent en Development), et un
/// comportement identique partout évite le bug « visible seulement en prod ».
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// CSP calibrée sur le fonctionnement réel de l'application (docs/Volume_7_Security.md) :
    /// <list type="bullet">
    /// <item><c>script-src 'self' 'unsafe-eval'</c> — Alpine.js (build standard, servi en local) évalue
    /// ses expressions <c>x-data</c>/<c>x-on</c> via <c>new Function</c> : sans 'unsafe-eval', chaque
    /// directive Alpine échoue. En revanche AUCUN 'unsafe-inline' : les scripts inline des vues ont été
    /// externalisés dans wwwroot/js — un <c>&lt;script&gt;</c> injecté par XSS ne s'exécutera pas.</item>
    /// <item><c>style-src 'self' 'unsafe-inline'</c> — quelques blocs <c>&lt;style&gt;</c> et attributs
    /// <c>style=</c> subsistent dans les vues (Caisse, Enrollments, Register).</item>
    /// <item><c>img-src … https: data:</c> — le logo d'établissement est une URL http(s) externe
    /// (UpdateCurrentSchoolCommand.LogoUrl), les aperçus utilisent des data-URI.</item>
    /// <item><c>frame-ancestors 'none'</c> — équivalent moderne de X-Frame-Options: DENY (les deux sont
    /// posés : les navigateurs récents lisent la CSP, les anciens l'en-tête historique).</item>
    /// </list>
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' https: data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Empêche le navigateur de « deviner » un type MIME (une réponse JSON reinterprétée en HTML
        // deviendrait un vecteur XSS).
        headers["X-Content-Type-Options"] = "nosniff";

        // L'application ne s'affiche dans AUCUNE iframe : pas de cas d'usage légitime, et cela ferme
        // le clickjacking.
        headers["X-Frame-Options"] = "DENY";

        // Obsolète pour les navigateurs récents (qui s'appuient sur la CSP) mais encore lu par
        // d'anciens moteurs — exigé par le ticket JGK-F01.
        headers["X-XSS-Protection"] = "1; mode=block";

        // Swagger UI (Development uniquement, voir Program.cs) embarque ses propres scripts et styles
        // inline : la CSP stricte le rendrait illisible. Les trois en-têtes ci-dessus s'appliquent, la
        // CSP seule est levée — en production, /swagger n'est pas mappé, l'exemption est donc inerte.
        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
        }

        await next(context);
    }
}
