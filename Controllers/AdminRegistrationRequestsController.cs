using SamaEcole.Application.Registration.Commands.ApproveRegistrationRequest;
using SamaEcole.Application.Registration.Commands.RejectRegistrationRequest;
using SamaEcole.Application.Registration.Queries.GetRegistrationRequests;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-I03 — revue et arbitrage des demandes d'inscription self-service. RÉSERVÉ AU SUPER ADMIN
/// (docs/Volume_4_API_Design.md §2, docs/Volume_7_Security.md §15) : c'est le seul rôle habilité à
/// créer un établissement, et l'approbation en crée un.
///
/// Contrôleur mince (AGENTS.md règle #8) : la transaction atomique (école + Directeur + abonnement),
/// l'audit et les e-mails vivent dans les Handlers.
/// </summary>
[ApiController]
[Route("api/v1/admin/registration-requests")]
[Authorize(Roles = nameof(Role.SuperAdmin))]
public class AdminRegistrationRequestsController(ISender mediator) : ControllerBase
{
    public record RejectRequest(string Reason);

    /// <summary>Liste filtrable par statut. Ne renvoie jamais le hash du mot de passe (voir la query).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RegistrationRequestListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] RegistrationRequestStatus? status, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRegistrationRequestsQuery(status), cancellationToken));

    /// <summary>
    /// Approuve : crée établissement + Directeur + abonnement (AwaitingPayment) dans UNE transaction
    /// atomique. Tout passe ou rien n'est créé (critère du ticket).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<ApproveRegistrationRequestResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ApproveRegistrationRequestCommand(id), cancellationToken));

    /// <summary>Rejette avec un motif OBLIGATOIRE (seule explication transmise au Directeur, via I02).</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] RejectRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new RejectRegistrationRequestCommand(id, request.Reason), cancellationToken);
        return NoContent();
    }
}
