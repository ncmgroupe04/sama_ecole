using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance : autorisée UNIQUEMENT si le Directeur de SON école a ouvert ce droit
/// (SchoolSettings.AllowFinanceToViewMonthlyStatus, faux par défaut). Tout autre rôle est refusé (échec fermé).
/// Le réglage est relu en base à chaque requête : le désactiver retire l'accès immédiatement, même à un comptable
/// dont le jeton est encore valide. Le Global Query Filter cantonne la lecture à l'école du JWT (règle #2).
/// </summary>
public class CanViewMonthlyStatusHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanViewMonthlyStatusRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanViewMonthlyStatusRequirement requirement)
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
            .Select(s => (bool?)s.AllowFinanceToViewMonthlyStatus)
            .FirstOrDefaultAsync() ?? SchoolSettingsDefaults.AllowFinanceToViewMonthlyStatus;

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
