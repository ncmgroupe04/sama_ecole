using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance : autorisée UNIQUEMENT si le Directeur de SON école a
/// coché « déléguer la suppression des frais à la Finance » (SchoolSettings.AllowFinanceToDeleteFees).
/// Même mécanique que CanModifyFeesHandler, avec son propre commutateur (règle de délégation
/// distincte de la simple modification de montant).
/// </summary>
public class CanDeleteFeesHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanDeleteFeesRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanDeleteFeesRequirement requirement)
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
            .Select(s => s.AllowFinanceToDeleteFees)
            .FirstOrDefaultAsync();

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
