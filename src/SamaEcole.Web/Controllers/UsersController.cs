using SamaEcole.Application.Users.Commands.ChangeUserStatus;
using SamaEcole.Application.Users.Commands.CreateUser;
using SamaEcole.Application.Users.Commands.ResetUserPassword;
using SamaEcole.Application.Users.Queries.GetUsers;
using SamaEcole.Application.Users.Queries.GetUserStatusHistory;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Gestion des comptes du personnel (création, cycle de vie, réinitialisation de mot de passe).
///
/// Réservé au DIRECTEUR. Le Super Admin est volontairement exclu : docs/Volume_7_Security.md §3 pose
/// qu'il « ne gère jamais » l'intérieur d'une école, et la matrice §4 réserve « Suspendre » au seul
/// Directeur. C'est cohérent avec la RLS : un Super Admin n'a pas de claim schoolId, sa session ne
/// voit donc aucun utilisateur d'école — il ne PEUT techniquement pas agir ici.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Roles = nameof(Role.Directeur))]
public class UsersController(ISender mediator) : ControllerBase
{
    public record ChangeStatusRequest(EntityStatus Status, string Reason);
    public record ResetPasswordRequest(string NewPassword);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetUsersQuery(), cancellationToken));

    /// <summary>Crée un compte Secrétariat/Finance/Enseignant. Mot de passe saisi directement par le Directeur (voir CreateUserCommand).</summary>
    [HttpPost]
    [ProducesResponseType<CreateUserResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(List), result);
    }

    [HttpPatch("{userId:guid}/status")]
    [ProducesResponseType<ChangeUserStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeStatus(
        Guid userId,
        [FromBody] ChangeStatusRequest request,
        CancellationToken cancellationToken)
    {
        // L'id vient de la route, jamais du corps : il ne doit pas pouvoir diverger.
        var result = await mediator.Send(
            new ChangeUserStatusCommand(userId, request.Status, request.Reason), cancellationToken);

        return Ok(result);
    }

    [HttpGet("{userId:guid}/status-history")]
    [ProducesResponseType<IReadOnlyList<UserStatusHistoryEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatusHistory(Guid userId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetUserStatusHistoryQuery(userId), cancellationToken));

    /// <summary>Réinitialise le mot de passe d'un compte (voir ResetUserPasswordCommand : sessions révoquées, auto-réinitialisation refusée).</summary>
    [HttpPatch("{userId:guid}/password")]
    [ProducesResponseType<ResetUserPasswordResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPassword(
        Guid userId,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResetUserPasswordCommand(userId, request.NewPassword), cancellationToken);

        return Ok(result);
    }
}
