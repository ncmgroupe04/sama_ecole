using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Demandes de changement d'abonnement de TOUTES les écoles — écran /admin/changements-abonnement. RÉSERVÉ AU SUPER ADMIN : le
/// Directeur envoie ses demandes par TenantSubscriptionController, lui seul les décide. Les lectures et la décision passent par des
/// fonctions SECURITY DEFINER (le Super Admin n'a aucun SchoolId de session).
/// </summary>
[ApiController]
[Route("api/v1/admin/subscription-change-requests")]
[Authorize(Roles = nameof(Role.SuperAdmin))]
public class AdminSubscriptionChangeRequestsController(ISender mediator) : ControllerBase
{
    public record DecisionBody(string? Note);

    /// <param name="status">Pending | Approved | Rejected | Cancelled ; absent = toutes.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SubscriptionChangeRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] SubscriptionChangeStatus? status, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminSubscriptionChangeRequestsQuery(status), cancellationToken));

    /// <summary>
    /// Valide la demande, une fois le règlement convenu : l'abonnement et les réglages de l'école prennent l'état demandé.
    /// 404 si elle n'existe plus ou est déjà décidée ; 409 si l'état de l'école a changé depuis (STALE_CHANGE_REQUEST), si son
    /// effectif dépasse la tranche demandée ou si un cycle retiré contient encore des classes.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<SubscriptionChangeRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecisionBody? body, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ApproveSubscriptionChangeRequestCommand(id, body?.Note), cancellationToken));

    /// <summary>Rejette la demande ; le motif est obligatoire (422 sinon), le Directeur le verra.</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<SubscriptionChangeRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecisionBody body, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RejectSubscriptionChangeRequestCommand(id, body?.Note ?? string.Empty), cancellationToken));
}
