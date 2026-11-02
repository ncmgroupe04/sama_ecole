using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance : autorisée sauf si le Directeur de SON école a retiré ce droit
/// (SchoolSettings.AllowFinanceToCancelPayslips, vrai par défaut). Même mécanique que CanCancelPaymentsHandler. Tout autre
/// rôle est refusé. Payer une fiche n'est pas concerné : il reste ouvert au Directeur et à la Finance.
/// </summary>
public class CanCancelPayslipsHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanCancelPayslipsRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanCancelPayslipsRequirement requirement)
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

        // Pas de ligne de paramètres du tout (école antérieure aux paramètres) : le défaut s'applique.
        var allowFinance = await dbContext.SchoolSettings
            .AsNoTracking()
            .Select(s => (bool?)s.AllowFinanceToCancelPayslips)
            .FirstOrDefaultAsync() ?? SchoolSettingsDefaults.AllowFinanceToCancelPayslips;

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
