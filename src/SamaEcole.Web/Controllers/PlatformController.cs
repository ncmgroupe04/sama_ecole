using SamaEcole.Application.Platform.Commands.AttachSchoolToUser;
using SamaEcole.Application.Platform.Commands.GrantComplimentaryAccess;
using SamaEcole.Application.Platform.Commands.ImpersonateSchool;
using SamaEcole.Application.Platform.Commands.SendSubscriptionReminder;
using SamaEcole.Application.Platform.Commands.TopUpSmsCredits;
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

    public record GrantComplimentaryAccessRequest(SubscriptionPlan Plan, int DurationMonths);

    /// <summary>
    /// Bouton « Offrir un accès » de l'écran Abonnements &amp; Facturation — module Tarification &amp;
    /// Promotions. N'échange aucun argent (voir GrantComplimentaryAccessCommand).
    /// </summary>
    [HttpPost("schools/{schoolId:guid}/complimentary-access")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GrantComplimentaryAccess(
        Guid schoolId, [FromBody] GrantComplimentaryAccessRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new GrantComplimentaryAccessCommand
            {
                SchoolId = schoolId,
                Plan = request.Plan,
                DurationMonths = request.DurationMonths
            },
            cancellationToken);

        return NoContent();
    }

    public record TopUpSmsCreditsRequest(int Segments);

    /// <summary>
    /// Crédite des SMS à une école (offre Premium). Réservé au Super Admin : c'est l'acte qui délivre
    /// une prestation payante, il ne peut pas appartenir au Directeur qui en bénéficie.
    /// </summary>
    [HttpPost("schools/{schoolId:guid}/sms-credits")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> TopUpSmsCredits(
        Guid schoolId, [FromBody] TopUpSmsCreditsRequest request, CancellationToken cancellationToken)
    {
        var newBalance = await mediator.Send(
            new TopUpSmsCreditsCommand { SchoolId = schoolId, Segments = request.Segments },
            cancellationToken);

        return Ok(new { creditBalance = newBalance });
    }

    public record AttachSchoolRequest(Guid SchoolId);

    /// <summary>
    /// Rattache un établissement supplémentaire à un compte (groupe scolaire). C'est ICI que se joue
    /// le contrôle commercial du multi-établissement — voir AttachSchoolToUserCommandHandler pour la
    /// raison pour laquelle la bascule elle-même n'est, elle, jamais verrouillée.
    /// </summary>
    [HttpPost("users/{userId:guid}/schools")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AttachSchoolToUser(
        Guid userId, [FromBody] AttachSchoolRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new AttachSchoolToUserCommand { UserId = userId, SchoolId = request.SchoolId },
            cancellationToken);

        return NoContent();
    }
}
