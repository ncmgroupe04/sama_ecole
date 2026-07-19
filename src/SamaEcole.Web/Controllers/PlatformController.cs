using SamaEcole.Application.Platform.Commands.ImpersonateSchool;
using SamaEcole.Application.Platform.Commands.SendSubscriptionReminder;
using SamaEcole.Application.Platform.Queries.GetPlatformActivity;
using SamaEcole.Application.Platform.Queries.GetPlatformDashboard;
using SamaEcole.Application.Platform.Queries.GetPlatformSubscriptions;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Console Super Admin — vue d'ensemble plateforme (dashboard), abonnements toutes écoles, journal
/// d'audit toutes écoles confondues (activité), et actions Établissements/Abonnements (impersonation,
/// rappel de paiement). RÉSERVÉ AU SUPER ADMIN, comme SchoolsController : c'est le seul rôle sans
/// SchoolId propre, et le seul autorisé à voir des données agrégées inter-écoles ou à agir pour le
/// compte d'une autre école (docs/Volume_7_Security.md §8/§15). Les endpoints de lecture passent par
/// les objets PostgreSQL des migrations AddPlatformAdminViews/AddPlatformSubscriptionsAndImpersonation
/// (vues + fonctions SECURITY DEFINER) — jamais une table tenant en direct.
/// </summary>
[ApiController]
[Route("api/v1/admin/platform")]
[Authorize(Roles = nameof(Role.SuperAdmin))]
public class PlatformController(ISender mediator) : ControllerBase
{
    [HttpGet("dashboard")]
    [ProducesResponseType<PlatformDashboardStatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPlatformDashboardQuery(), cancellationToken));

    [HttpGet("activity")]
    [ProducesResponseType<PaginatedGlobalAuditLogs>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActivity(
        [FromQuery] GetPlatformActivityQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpGet("subscriptions")]
    [ProducesResponseType<IReadOnlyList<PlatformSubscriptionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSubscriptions(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPlatformSubscriptionsQuery(), cancellationToken));

    [HttpPost("subscriptions/{schoolId:guid}/remind")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemindSubscription(Guid schoolId, CancellationToken cancellationToken)
    {
        await mediator.Send(new SendSubscriptionReminderCommand(schoolId), cancellationToken);
        return NoContent();
    }

    [HttpPost("schools/{schoolId:guid}/impersonate")]
    [ProducesResponseType<ImpersonateSchoolResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImpersonateSchool(Guid schoolId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ImpersonateSchoolCommand(schoolId), cancellationToken));
}
