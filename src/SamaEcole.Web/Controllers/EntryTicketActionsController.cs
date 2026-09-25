using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Acceptation et annulation d'un billet d'entrée visant un cours (Évolution N°5). Contrôleur mince : aucune
/// logique métier ici (règle #8) ; l'école vient du JWT, l'auteur aussi (règle #10).
///
/// À PART de <see cref="BilletsController"/> à dessein : celui-ci est réservé à la Vie Scolaire, au Secrétariat et
/// à la Direction, alors que l'acceptation revient à l'ENSEIGNANT du cours — un attribut de rôle sur une action
/// s'ajoute à celui de la classe, il ne l'élargit pas. Même préfixe de route, aucune collision d'actions.
///
/// ACCEPTER : Enseignant (borné à SES cours dans le handler, 403 sinon) et Directeur.
/// ANNULER  : Vie Scolaire (Surveillant) et Directeur — l'enseignant accepte, il n'annule pas.
/// </summary>
[ApiController]
[Route("api/v1/billets")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class EntryTicketActionsController(ISender mediator) : ControllerBase
{
    /// <summary>Accepte l'élève en classe. Idempotent. 422 si le billet est annulé ou ne vise aucun cours.</summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Roles = $"{nameof(Role.Enseignant)},{nameof(Role.Directeur)}")]
    [ProducesResponseType<EntryTicketActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Accept(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new AcceptEntryTicketCommand(id), cancellationToken));

    /// <summary>Annule un billet non accepté ; la ligne d'appel retrouve son statut d'avant. 422 si déjà accepté.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = $"{nameof(Role.Surveillant)},{nameof(Role.Directeur)}")]
    [ProducesResponseType<EntryTicketActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CancelEntryTicketCommand(id), cancellationToken));
}
