using System.Text.Json;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Un refus d'autorisation ASP.NET Core produit un 403 au CORPS VIDE. Pour une fonctionnalité hors
/// formule, le client ne saurait alors pas distinguer « rôle insuffisant » de « il faut passer à
/// Premium » — et ne pourrait pas proposer la montée en gamme.
///
/// Ce gestionnaire n'intercepte QUE l'échec d'une <see cref="FeatureRequirement"/> et lui donne le
/// format d'erreur normalisé (docs/Volume_4_API_Design.md §0.4, AGENTS.md règle #9), avec le code
/// FEATURE_NOT_IN_PLAN et la formule minimale requise. Tout le reste — succès, autres refus,
/// challenges — est délégué au gestionnaire par défaut, inchangé.
/// </summary>
public class FeatureAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var failedFeature = authorizeResult.AuthorizationFailure?.FailedRequirements
            .OfType<FeatureRequirement>()
            .Select(r => (Feature?)r.Feature)
            .FirstOrDefault();

        // Non authentifié : c'est un challenge (401), pas un problème de formule — le gestionnaire par
        // défaut sait seul le produire correctement.
        if (failedFeature is not { } feature || authorizeResult.Challenged)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var minimumPlan = PlanFeatures.MinimumPlanFor(feature);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            code = "FEATURE_NOT_IN_PLAN",
            message = $"Cette fonctionnalité n'est pas incluse dans votre formule. Passez à la formule {minimumPlan} pour en bénéficier.",
            details = new { feature = feature.ToString(), requiredPlan = minimumPlan.ToString() },
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
