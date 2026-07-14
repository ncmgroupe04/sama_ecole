using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using SamaEcole.Application.Classrooms.Queries.GetClassrooms;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-C02 — /classrooms (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
/// </summary>
[ApiController]
[Route("api/v1/classrooms")]
[Authorize]
public class ClassroomsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClassroomDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassroomsQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreateClassroomResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateClassroomCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }
}
