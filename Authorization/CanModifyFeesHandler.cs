using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance : autorisée UNIQUEMENT si le Directeur de SON école a
/// coché « déléguer la modification des frais à la Finance » (SchoolSettings.AllowFinanceToModifyFees).
///
/// La lecture passe par IApplicationDbContext, pas par un rôle codé en dur — même mécanique que
/// CanManageGradingScaleHandler (JGK-G02). Le Global Query Filter EF Core cantonne déjà la requête
/// à l'école du JWT courant (AGENTS.md règle #2).
///
/// Absence de ligne SchoolSettings : FirstOrDefaultAsync sur un bool renvoie `false` par défaut —
/// même comportement sûr que SchoolSettingsDefaults.AllowFinanceToModifyFees, la délégation reste
/// fermée tant que le Directeur ne l'a pas explicitement activée.
/// </summary>
public class CanModifyFeesHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanModifyFeesRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanModifyFeesRequirement requirement)
    {
        if (context.User.IsInRole(nameof(Role.Directeur)))
        {
            context.Succeed(requirement);
            return;
        }

        if (!context.User.IsInRole(nameof(Role.Finance)))
        {
            return;
        }

        var allowFinance = await dbContext.SchoolSettings
            .AsNoTracking()
            .Select(s => s.AllowFinanceToModifyFees)
            .FirstOrDefaultAsync();

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
