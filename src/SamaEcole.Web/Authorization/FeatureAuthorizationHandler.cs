using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Évalue <see cref="FeatureRequirement"/> : la formule de l'abonnement de l'école courante
/// inclut-elle la fonctionnalité (matrice <see cref="PlanFeatures"/>, partagée avec l'interface) ?
///
/// La formule est lue EN BASE à chaque requête, jamais depuis le JWT — exactement le raisonnement de
/// SubscriptionAwaitingPaymentMiddleware : un jeton émis avant un changement de formule reste valide
/// après, et c'est l'abonnement réel qui doit gouverner l'accès. Le JWT ne porte donc toujours que
/// sub/schoolId/role (AGENTS.md règle #10).
///
/// REFUS PAR DÉFAUT en l'absence d'abonnement : une fonctionnalité facturée ne doit jamais s'ouvrir
/// faute d'information. Toute école créée par les deux chemins d'entrée (CreateSchoolCommand,
/// ApproveRegistrationRequest) en possède un ; les jeux de démonstration sont dotés d'une formule
/// Premium par DbSeeder pour que le développement reste complet.
/// </summary>
public class FeatureAuthorizationHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : AuthorizationHandler<FeatureRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, FeatureRequirement requirement)
    {
        // Le Super Admin n'a aucun SchoolId ni abonnement propre : il opère la plateforme, il ne
        // consomme la formule de personne. Quand il infiltre une école (ImpersonateSchoolCommand), il
        // porte un jeton de Directeur AVEC schoolId — le verrouillage s'applique alors normalement,
        // et c'est bien la formule de l'école visitée qui décide.
        if (context.User.IsInRole(nameof(Role.SuperAdmin)))
        {
            context.Succeed(requirement);
            return;
        }

        if (tenantProvider.CurrentSchoolId is not { } schoolId)
        {
            return;
        }

        // Subscription EST une ITenantEntity : Global Query Filter + policy RLS bornent déjà la lecture
        // à l'école du JWT (comme SubscriptionAwaitingPaymentMiddleware). Le Where explicite reste, il
        // porte sur le même claim que TenantProvider.
        var plan = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => (SubscriptionPlan?)s.Plan)
            .SingleOrDefaultAsync();

        if (plan is { } currentPlan && PlanFeatures.Includes(currentPlan, requirement.Feature))
        {
            context.Succeed(requirement);
        }
    }
}
