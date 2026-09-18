using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Évalue <see cref="ModuleRequirement"/> : le Directeur de l'école courante a-t-il activé ce module
/// (SchoolSettings.IsXxxEnabled) ? Miroir de <see cref="FeatureAuthorizationHandler"/>, mais lu depuis
/// les RÉGLAGES de l'école plutôt que depuis sa formule d'abonnement — deux axes indépendants.
///
/// DIFFÉRENCE DÉLIBÉRÉE avec FeatureAuthorizationHandler : celui-ci refuse par défaut en l'absence de
/// ligne (une fonctionnalité facturée ne doit jamais s'ouvrir faute d'information). Ici c'est
/// l'inverse — Pédagogie et Finance sont le socle métier, actives par défaut (voir
/// SchoolSettingsDefaults, même repli que GetSchoolSettingsQueryHandler) : une école sans ligne
/// `school_settings` (jamais ouvert Paramètres depuis JGK-B02) ne doit PAS se retrouver privée de
/// Pédagogie/Finance faute d'avoir sauvegardé un réglage qu'elle ignore.
/// </summary>
public class ModuleAuthorizationHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : AuthorizationHandler<ModuleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ModuleRequirement requirement)
    {
        // Le Super Admin n'a aucun SchoolId ni réglage propre : il opère la plateforme. En
        // infiltration (ImpersonateSchoolCommand), il porte un jeton de Directeur AVEC schoolId — le
        // verrouillage s'applique alors normalement, sur les réglages de l'école visitée.
        if (context.User.IsInRole(nameof(Role.SuperAdmin)))
        {
            context.Succeed(requirement);
            return;
        }

        if (tenantProvider.CurrentSchoolId is not { } schoolId)
        {
            return;
        }

        // SchoolSettings EST une ITenantEntity : Global Query Filter + policy RLS bornent déjà la
        // lecture à l'école du JWT.
        var settings = await dbContext.SchoolSettings
            .AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .SingleOrDefaultAsync();

        var enabled = requirement.Module switch
        {
            SchoolModule.Pedagogy => settings?.IsPedagogyEnabled ?? SchoolSettingsDefaults.IsPedagogyEnabled,
            SchoolModule.Finance => settings?.IsFinanceEnabled ?? SchoolSettingsDefaults.IsFinanceEnabled,
            SchoolModule.Internat => settings?.IsInternatEnabled ?? SchoolSettingsDefaults.IsInternatEnabled,
            SchoolModule.Coran => settings?.IsCoranModuleEnabled ?? SchoolSettingsDefaults.IsCoranModuleEnabled,
            _ => false
        };

        if (enabled)
        {
            context.Succeed(requirement);
        }
    }
}
