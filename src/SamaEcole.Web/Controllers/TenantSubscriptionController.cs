using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Abonnement commercial de l'établissement COURANT : sa position par rapport au quota d'élèves, et les demandes de changement
/// (profil, cycles, tranche, modules) que le Directeur envoie au lieu de les modifier directement — le Super Admin les valide
/// (voir AdminSubscriptionChangeRequestsController). « courant » vient du claim JWT, jamais d'un paramètre (AGENTS.md règle #10).
/// </summary>
[ApiController]
[Route("api/v1/tenant-subscription")]
[Authorize]
public class TenantSubscriptionController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Effectif, plafond, tolérance, état, tranche suggérée et demande en cours : de quoi afficher le bandeau de quota du tableau
    /// de bord. Directeur et Secrétariat — ceux qui créent des élèves. 204 tant que l'abonnement n'est pas actif.
    /// </summary>
    [HttpGet("quota")]
    [Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}")]
    [ProducesResponseType<QuotaOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetQuota(CancellationToken cancellationToken)
    {
        var overview = await mediator.Send(new GetQuotaOverviewQuery(), cancellationToken);

        return overview is null ? NoContent() : Ok(overview);
    }

    [HttpGet("change-requests")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<IReadOnlyList<SubscriptionChangeRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChangeRequests(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetSubscriptionChangeRequestsQuery(), cancellationToken));

    /// <summary>Ce que changerait et coûterait cette modification, sans rien écrire (tarif ajusté, réduction, blocages).</summary>
    [HttpPost("change-requests/preview")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SubscriptionChangePreview>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Preview([FromBody] SubscriptionChangeSet change, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PreviewSubscriptionChangeQuery(change), cancellationToken));

    public record CreateChangeRequestBody(SubscriptionChangeSet Change, string? Note);

    /// <summary>
    /// Envoie une demande de changement. Elle ne modifie RIEN : le Super Admin la valide une fois le règlement convenu.
    /// 409 CHANGE_REQUEST_PENDING s'il y en a déjà une ; 422 si elle ne change rien ou se heurte à un blocage.
    /// </summary>
    [HttpPost("change-requests")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SubscriptionChangeRequestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateChangeRequest(
        [FromBody] CreateChangeRequestBody body, CancellationToken cancellationToken)
    {
        var created = await mediator.Send(
            new CreateSubscriptionChangeRequestCommand(body.Change ?? new SubscriptionChangeSet(), body.Note), cancellationToken);

        return CreatedAtAction(nameof(GetChangeRequests), created);
    }

    [HttpPost("change-requests/{id:guid}/cancel")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SubscriptionChangeRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelChangeRequest(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CancelSubscriptionChangeRequestCommand(id), cancellationToken));
}
