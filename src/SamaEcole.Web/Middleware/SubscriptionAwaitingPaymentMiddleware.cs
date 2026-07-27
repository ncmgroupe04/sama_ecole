using System.Text.Json;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Middleware;

/// <summary>
/// Ticket JGK-I04 — restriction d'accès tant que <c>Subscriptions.Status = AwaitingPayment</c>
/// (docs/Volume_7_Security.md §12bis, AGENTS.md règle #11).
///
/// Vérifiée à CHAQUE requête, jamais seulement à la connexion : « un token JWT émis avant paiement
/// reste valide après paiement » et réciproquement — c'est le statut de l'abonnement EN BASE, pas le
/// contenu du token, qui gouverne l'accès (§12bis). Le JWT ne porte donc délibérément aucun claim
/// d'abonnement (AGENTS.md règle #10 : le token ne porte que sub/schoolId/role) ; l'interroger à chaque
/// requête est le prix de cette fraîcheur, payé une fois ici plutôt que dans chaque Handler.
///
/// Portée : uniquement `/api/v1/*`. Les pages Razor (PagesController) sont des gabarits vides et
/// anonymes qui ne portent aucune donnée sensible (voir son commentaire de classe) — le JWT vit dans
/// localStorage et ne voyage jamais sur une navigation classique, donc une page ne peut de toute façon
/// pas être filtrée ici faute d'identité côté serveur. La redirection visible par l'utilisateur est
/// assurée côté client par wwwroot/js/api.js, qui intercepte le code SUBSCRIPTION_AWAITING_PAYMENT
/// ci-dessous exactement comme il intercepte déjà un 401.
/// </summary>
public class SubscriptionAwaitingPaymentMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Exceptions obligatoires (§12bis, ticket JGK-I04) : session (connexion/déconnexion/renouvellement)
    /// et paiement d'abonnement (JGK-I05/I06, pas encore livré — allowlisté par avance : la restriction
    /// ne doit jamais bloquer la seule voie de sortie du mode restreint). Le profil utilisateur, cité
    /// par le même paragraphe, n'a PAS encore de route dédiée dans ce projet (aucun GET/PUT /users/me) :
    /// rien à exempter de plus tant qu'elle n'existe pas.
    /// </summary>
    private static readonly string[] AllowedPrefixes =
    [
        "/api/v1/auth/",
        "/api/v1/subscriptions/"
    ];

    public async Task InvokeAsync(HttpContext context, IApplicationDbContext dbContext)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Hors API (pages Razor, wwwroot) : voir le commentaire de classe — rien à filtrer ici.
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || AllowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var schoolIdClaim = context.User.FindFirst("schoolId");

        // Pas de tenant : requête anonyme (login...) ou Super Admin (SchoolId toujours NULL, aucun
        // abonnement ne le concerne) — rien à restreindre pour l'un comme pour l'autre.
        if (schoolIdClaim is null || !Guid.TryParse(schoolIdClaim.Value, out var schoolId))
        {
            await next(context);
            return;
        }

        // Subscription EST une ITenantEntity : Global Query Filter + policy RLS bornent déjà la lecture
        // à l'école du JWT (AGENTS.md règle #2). Le Where explicite est conservé — il porte sur le MÊME
        // claim que TenantProvider, et rend lisible ici la ligne qu'on interroge.
        var status = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => (SubscriptionStatus?)s.Status)
            .SingleOrDefaultAsync(context.RequestAborted);

        // Aucun abonnement pour cette école : établissement créé hors du parcours self-service
        // (JGK-B01, ou données de démonstration/tests) — rien à restreindre. Seul un abonnement
        // EXISTANT et explicitement AwaitingPayment déclenche le blocage (§12bis).
        if (status is not SubscriptionStatus.AwaitingPayment)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            code = "SUBSCRIPTION_AWAITING_PAYMENT",
            message = "Votre abonnement est en attente de paiement. Seule la régularisation de votre abonnement est accessible tant que le premier paiement n'est pas confirmé.",
            details = (object?)null,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
