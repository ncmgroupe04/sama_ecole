using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance (le caissier) : autorisée sauf si le Directeur de SON école a retiré ce droit
/// (SchoolSettings.AllowFinanceToCancelPayments, vrai par défaut). Même mécanique que CanDeleteFeesHandler, avec le défaut
/// inverse : annuler son propre versement est le geste quotidien du caissier. Le délai de 48 h et la contre-passation
/// (Directeur seul) sont tenus par CancelPaymentCommandHandler, pas ici.
/// </summary>
public class CanCancelPaymentsHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanCancelPaymentsRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanCancelPaymentsRequirement requirement)
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
            .Select(s => (bool?)s.AllowFinanceToCancelPayments)
            .FirstOrDefaultAsync() ?? SchoolSettingsDefaults.AllowFinanceToCancelPayments;

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
