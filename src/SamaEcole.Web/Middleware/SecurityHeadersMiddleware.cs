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
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-src 'self' blob:; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none';";

    /// <summary>
    /// Politique de la SEULE page d'inscription quand Cloudflare Turnstile est configuré : le widget charge un script,
    /// ouvre un cadre et appelle l'API de Cloudflare. L'allocation est limitée à cette page et à cette origine — le reste
    /// du site garde la politique stricte ci-dessus, sans aucune origine tierce en script.
    /// </summary>
    private const string TurnstileOrigin = "https://challenges.cloudflare.com";

    private static readonly string ContentSecurityPolicyWithTurnstile = ContentSecurityPolicy
        .Replace("script-src 'self' 'unsafe-eval';", $"script-src 'self' 'unsafe-eval' {TurnstileOrigin};")
        .Replace("connect-src 'self';", $"connect-src 'self' {TurnstileOrigin};")
        .Replace("frame-src 'self' blob:;", $"frame-src 'self' blob: {TurnstileOrigin};");

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Canonical host redirect: forwarded headers are processed earlier in Program.cs, and this
        // middleware runs before the remaining application pipeline. Keep both path and query string.
        if (string.Equals(context.Request.Host.Host, "www.unikol.net", StringComparison.OrdinalIgnoreCase))
        {
            var target = $"https://unikol.net{context.Request.Path}{context.Request.QueryString}";
            context.Response.Redirect(target, permanent: true);
            return;
        }

        // Empêche le navigateur de « deviner » un type MIME (une réponse JSON réinterprétée en HTML
        // deviendrait un vecteur XSS).
        headers["X-Content-Type-Options"] = "nosniff";

        // L'application bloque strictement l'affichage en iframe (DENY).
        // La prévisualisation des PDF contourne cette restriction sans amoindrir la sécurité globale
        // du site en convertissant d'abord le flux binaire en Blob URL 100 % local (côté navigateur).
        headers["X-Frame-Options"] = "DENY";

        // Obsolète pour les navigateurs récents (qui s'appuient sur la CSP) mais encore lu par
        // d'anciens moteurs — exigé par le ticket JGK-F01.
        headers["X-XSS-Protection"] = "1; mode=block";

        // N'expose l'URL complète qu'en navigation MÊME ORIGINE : vers une origine tierce (logo
        // d'établissement en img-src https:, lien sortant), seul le domaine part, et rien en clair.
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Coupe les API navigateur puissantes que l'application n'utilise pas : la saisie GPS SIMEN et
        // la lecture d'un QR de mutation passent par un formulaire ou l'appareil photo natif du
        // téléphone, jamais par ces API web. Réduit la surface d'une éventuelle injection de script.
        headers["Permissions-Policy"] =
            "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), " +
            "fullscreen=(self), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), " +
            "midi=(), payment=(), usb=(), xr-spatial-tracking=()";

        // Swagger UI (Development uniquement, voir Program.cs) embarque ses propres scripts et styles
        // inline : la CSP stricte le rendrait illisible. Les trois en-têtes ci-dessus s'appliquent, la
        // CSP seule est levée — en production, /swagger n'est pas mappé, l'exemption est donc inerte.
        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            var allowTurnstile = context.Request.Path.Equals("/inscription", StringComparison.OrdinalIgnoreCase)
                && context.RequestServices.GetService<Microsoft.Extensions.Options.IOptions<Security.TurnstileOptions>>()?.Value.IsEnabled == true;

            headers["Content-Security-Policy"] = allowTurnstile ? ContentSecurityPolicyWithTurnstile : ContentSecurityPolicy;
        }

        await next(context);
    }
}
