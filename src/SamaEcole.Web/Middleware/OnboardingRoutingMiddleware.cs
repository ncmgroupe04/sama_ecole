using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Middleware;

/// <summary>
/// Onboarding & Pricing SaaS — tant que la souscription commerciale de l'école est
/// <see cref="TenantSubscriptionStatus.PendingOnboarding"/> (le Directeur n'a choisi ni son profil ni sa
/// tranche d'effectif), TOUTE l'API est refusée en 403 <c>ONBOARDING_REQUIRED</c>, hormis la session, les
/// endpoints d'Onboarding eux-mêmes et le paiement de l'abonnement.
///
/// Même construction que <see cref="SubscriptionAwaitingPaymentMiddleware"/> : le statut EN BASE est relu à
/// chaque requête (le JWT ne porte aucun claim d'abonnement — AGENTS.md règle #10), et seul `/api/*` est
/// filtré. Les pages Razor sont des gabarits anonymes sans donnée : la redirection visible est faite côté
/// client (api.js) à partir du code d'erreur, exactement comme pour SUBSCRIPTION_AWAITING_PAYMENT. Le
/// <c>details.redirectTo</c> de la réponse indique la destination.
///
/// Pourquoi `/api/v1/subscriptions/` reste ouvert : une école nouvelle est AUSSI en
/// <c>Subscriptions.Status = AwaitingPayment</c> ; ce middleware-là n'ouvre à son tour que l'Onboarding.
/// Les deux listes blanches se complètent, de sorte que l'ordre « payer puis configurer » ou
/// « configurer puis payer » n'engendre aucun blocage mutuel.
///
/// Aucune souscription pour l'école (établissement antérieur à la fonctionnalité, jeu de données de test) :
/// rien à restreindre, comme le font les autres middlewares de ce dossier. La migration de reprise a doté
/// toutes les écoles existantes ; toute création d'école en provisionne une. Pour autant, créer un élève
/// reste refusé sans souscription (voir StudentQuotaGuard) : l'accès n'est pas bloqué, la facturation si.
/// </summary>
public class OnboardingRoutingMiddleware(RequestDelegate next)
{
    public const string RedirectPath = "/onboarding/select-profile";

    private static readonly string[] AllowedPrefixes =
    [
        "/api/v1/auth/",
        "/api/v1/onboarding/",
        "/api/v1/subscriptions/"
    ];

    public async Task InvokeAsync(HttpContext context, IApplicationDbContext dbContext)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || AllowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        // Pas de tenant : requête anonyme (login...) ou Super Admin (SchoolId toujours NULL) — rien à restreindre.
        var schoolIdClaim = context.User.FindFirst("schoolId");

        if (schoolIdClaim is null || !Guid.TryParse(schoolIdClaim.Value, out var schoolId))
        {
            await next(context);
            return;
        }

        // TenantSubscription EST une ITenantEntity : Global Query Filter + policy RLS bornent déjà la lecture
        // à l'école du JWT (règle #2). Le Where explicite rend lisible la ligne interrogée.
        var status = await dbContext.TenantSubscriptions
            .AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => (TenantSubscriptionStatus?)s.Status)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (status is not TenantSubscriptionStatus.PendingOnboarding)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            code = "ONBOARDING_REQUIRED",
            message = "Choisissez le profil et la tranche d'effectif de votre établissement pour accéder à l'application.",
            details = new { redirectTo = RedirectPath },
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
