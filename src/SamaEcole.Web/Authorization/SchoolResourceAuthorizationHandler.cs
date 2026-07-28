using SamaEcole.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Autorisation resource-based générique (AGENTS.md règle #10) : la ressource — un <see cref="Guid"/>
/// SchoolId lu dans le segment de route par le contrôleur appelant — ne passe que si elle correspond au
/// tenant réel de la session (<see cref="ITenantProvider.CurrentSchoolId"/>, dérivé du claim JWT, jamais
/// d'un paramètre modifiable par le client). Un désaccord — un Directeur de l'école A visant l'école B
/// dans l'URL — est refusé même si la RLS PostgreSQL empêcherait de toute façon toute fuite de données :
/// mieux vaut un 403 explicite qu'une divergence silencieuse entre l'URL et la donnée réellement lue.
/// </summary>
public class SchoolResourceAuthorizationHandler(ITenantProvider tenantProvider)
    : AuthorizationHandler<SchoolResourceRequirement, Guid>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SchoolResourceRequirement requirement, Guid resourceSchoolId)
    {
        if (tenantProvider.CurrentSchoolId == resourceSchoolId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
