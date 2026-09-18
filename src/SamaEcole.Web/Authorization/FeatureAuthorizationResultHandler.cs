using System.Text.Json;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Un refus d'autorisation ASP.NET Core produit un 403 au CORPS VIDE. Pour une fonctionnalité hors
/// formule ou un module désactivé par le Directeur, le client ne saurait alors pas distinguer « rôle
/// insuffisant » d'« il faut passer à Premium »/« ce module est désactivé » — et ne pourrait pas
/// proposer la montée en gamme ni renvoyer vers Paramètres.
///
/// Ce gestionnaire n'intercepte QUE l'échec d'une <see cref="FeatureRequirement"/> (code
/// FEATURE_NOT_IN_PLAN, formule minimale requise) ou d'une <see cref="ModuleRequirement"/> (code
/// MODULE_DISABLED) et leur donne le format d'erreur normalisé (docs/Volume_4_API_Design.md §0.4,
/// AGENTS.md règle #9). Un seul <see cref="IAuthorizationMiddlewareResultHandler"/> pouvant être
/// enregistré par application (même contrainte que <see cref="FeaturePolicyProvider"/>), les deux
/// axes partagent ce gestionnaire plutôt qu'un chacun. Tout le reste — succès, autres refus,
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
        // Non authentifié : c'est un challenge (401), pas un problème de formule/module — le
        // gestionnaire par défaut sait seul le produire correctement.
        if (authorizeResult.Challenged)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var failedFeature = authorizeResult.AuthorizationFailure?.FailedRequirements
            .OfType<FeatureRequirement>()
            .Select(r => (Feature?)r.Feature)
            .FirstOrDefault();

        if (failedFeature is { } feature)
        {
            var minimumPlan = PlanFeatures.MinimumPlanFor(feature);

            await WriteJsonAsync(context, StatusCodes.Status403Forbidden, new
            {
                code = "FEATURE_NOT_IN_PLAN",
                message = $"Cette fonctionnalité n'est pas incluse dans votre formule. Passez à la formule {minimumPlan} pour en bénéficier.",
                details = new { feature = feature.ToString(), requiredPlan = minimumPlan.ToString() },
                traceId = context.TraceIdentifier
            });
            return;
        }

        var failedModule = authorizeResult.AuthorizationFailure?.FailedRequirements
            .OfType<ModuleRequirement>()
            .Select(r => (SchoolModule?)r.Module)
            .FirstOrDefault();

        if (failedModule is { } module)
        {
            await WriteJsonAsync(context, StatusCodes.Status403Forbidden, new
            {
                code = "MODULE_DISABLED",
                message = $"Le module « {module} » est désactivé pour votre établissement. Un Directeur peut l'activer depuis Paramètres › Modules.",
                details = new { module = module.ToString() },
                traceId = context.TraceIdentifier
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteJsonAsync(HttpContext context, int statusCode, object payload)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
