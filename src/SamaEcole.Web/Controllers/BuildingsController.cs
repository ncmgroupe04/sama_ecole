using SamaEcole.Application.Buildings.Commands.CreateBuilding;
using SamaEcole.Application.Buildings.Commands.DeleteBuilding;
using SamaEcole.Application.Buildings.Commands.UpdateBuilding;
using SamaEcole.Application.Buildings.Queries.GetBuildingsWithRooms;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Infrastructures — /buildings. Contrôleur mince : aucune logique métier ici (AGENTS.md
/// règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// LECTURE ouverte à tout utilisateur authentifié (comme Classrooms/Subjects). CRÉER, CORRIGER ou
/// ARCHIVER un bâtiment est réservé au Directeur et au Secrétariat — même matrice que Classrooms
/// (docs/Volume_7_Security.md §15bis), aucune spec dédiée n'existant encore pour ce module.
/// </summary>
[ApiController]
[Route("api/v1/buildings")]
[Authorize]
public class BuildingsController(ISender mediator) : ControllerBase
{
    public record UpdateBuildingRequest(string Name, string? Description, uint RowVersion);

    private const string ManageRoles = "Directeur,Secretariat";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BuildingWithRoomsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetBuildingsWithRoomsQuery(), cancellationToken));

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<BuildingResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBuildingCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<BuildingResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateBuildingRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateBuildingCommand(id, request.Name, request.Description, request.RowVersion),
            cancellationToken));

    /// <summary>Archive (soft delete) un bâtiment. Refusé en 409 si des salles y sont encore rattachées.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteBuildingCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }
}
