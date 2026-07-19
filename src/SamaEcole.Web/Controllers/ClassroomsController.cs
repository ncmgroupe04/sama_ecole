using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using SamaEcole.Application.Classrooms.Commands.DeleteClassroom;
using SamaEcole.Application.Classrooms.Commands.UpdateClassroom;
using SamaEcole.Application.Classrooms.Queries.GetClassrooms;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-C02 — /classrooms (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// LECTURE et CRÉATION ouvertes à tout utilisateur authentifié (comportement historique inchangé).
/// CORRIGER ou ARCHIVER une classe déjà créée est réservé au Directeur et au Secrétariat — ce sont ces
/// deux rôles qui gèrent l'organisation des classes au quotidien.
/// </summary>
[ApiController]
[Route("api/v1/classrooms")]
[Authorize]
public class ClassroomsController(ISender mediator) : ControllerBase
{
    public record UpdateClassroomRequest(string Name, string Level, int Capacity, uint RowVersion);

    private const string ManageRoles = "Directeur,Secretariat";

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

    /// <summary>Corrige le libellé/niveau/capacité d'une classe déjà créée (erreur de saisie).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<ClassroomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateClassroomRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateClassroomCommand(id, request.Name, request.Level, request.Capacity, request.RowVersion),
            cancellationToken));

    /// <summary>
    /// Archive (soft delete) une classe créée par erreur. Refusée en 409 si des élèves y sont encore
    /// rattachés (DeleteClassroomCommandHandler). `rowVersion` en query string, comme DELETE /grades/{id}.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteClassroomCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }
}
