using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Ticket JGK-G02 — Directeur : toujours autorisé. Secrétariat : autorisé UNIQUEMENT si le Directeur
/// de SON école a coché « déléguer la notation au Secrétariat » (SchoolSettings.AllowSecretaryToManageGrading).
///
/// La lecture passe par IApplicationDbContext, pas par un rôle codé en dur : c'est tout le sens de ce
/// ticket (une propriété par école, pas un rôle figé dans l'attribut). Le Global Query Filter EF Core
/// cantonne déjà la requête à l'école du JWT courant (AGENTS.md règle #2) — aucun SchoolId à repasser
/// à la main ici, contrairement à ITenantProvider.CurrentSchoolId utilisé ailleurs pour des INSERT.
///
/// Absence de ligne SchoolSettings (école jamais configurée) : FirstOrDefaultAsync sur un bool renvoie
/// `false` par défaut — même comportement sûr que SchoolSettingsDefaults.AllowSecretaryToManageGrading,
/// la délégation reste fermée tant que le Directeur ne l'a pas explicitement activée.
/// </summary>
public class CanManageGradingScaleHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanManageGradingScaleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanManageGradingScaleRequirement requirement)
    {
        if (context.User.IsInRole(nameof(Role.Directeur)))
        {
            context.Succeed(requirement);
            return;
        }

        if (!context.User.IsInRole(nameof(Role.Secretariat)))
        {
            return;
        }

        var allowSecretary = await dbContext.SchoolSettings
            .AsNoTracking()
            .Select(s => s.AllowSecretaryToManageGrading)
            .FirstOrDefaultAsync();

        if (allowSecretary)
        {
            context.Succeed(requirement);
        }
    }
}
