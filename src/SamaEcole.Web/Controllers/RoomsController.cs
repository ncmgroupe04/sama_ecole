using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Rooms.Commands.UnlinkRoomDependencies;
using SamaEcole.Application.Rooms.Queries.GetRoomDeletionImpact;
using SamaEcole.Application.Rooms.Commands.RestoreRoom;
using SamaEcole.Application.Rooms.Queries.GetDeletedRooms;
using SamaEcole.Application.Rooms.Commands.CreateRoom;
using SamaEcole.Application.Rooms.Commands.DeleteRoom;
using SamaEcole.Application.Rooms.Commands.UpdateRoom;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Infrastructures — /rooms. Route PLATE (pas de nesting sous /buildings/{id}/rooms) :
/// aucune autre ressource de ce codebase n'imbrique une collection enfant dans son URL (même les
/// sous-ressources d'une inscription, ex. /enrollments/{id}/exeat, ne sont que des ACTIONS, jamais une
/// collection) — BuildingId voyage dans le corps à la création, jamais dans l'URL.
///
/// Une salle se lit via GetBuildingsWithRoomsQuery (BuildingsController), déjà imbriquée dans son
/// bâtiment : ce contrôleur ne porte donc aucun GET de liste, seulement les écritures.
///
/// Mêmes rôles que BuildingsController (Directeur/Secretariat).
/// </summary>
[ApiController]
[Route("api/v1/rooms")]
[Authorize]
public class RoomsController(ISender mediator) : ControllerBase
{
    public record CreateRoomRequest(string Name, int Capacity, RoomType Type, Guid BuildingId);
    public record UpdateRoomRequest(string Name, int Capacity, RoomType Type, uint RowVersion);

    private const string ManageRoles = "Directeur,Secretariat";

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<RoomResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateRoomCommand
            {
                Name = request.Name,
                Capacity = request.Capacity,
                Type = request.Type,
                BuildingId = request.BuildingId
            }, cancellationToken);

        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<RoomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateRoomRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateRoomCommand(id, request.Name, request.Capacity, request.Type, request.RowVersion),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteRoomCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Ce qui empêche ou accompagne la suppression d'une salle (spec 2026-10-17, §3) : blocage financier ou liste précise des
    /// liens à rompre. Lecture seule.
    /// </summary>
    [HttpGet("{id:guid}/deletion-impact")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DeletionImpact>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RoomDeletionImpact(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRoomDeletionImpactQuery(id), cancellationToken));

    /// <summary>
    /// Rupture EXPLICITE des liens de la salle (archivage ou détachement, tracé). Ne supprime pas l'élément.
    /// 409 : blocage financier ou lien non rompable.
    /// </summary>
    [HttpPost("{id:guid}/unlink-dependencies")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DeletionImpact>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkRoomDependencies(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UnlinkRoomDependenciesCommand(id), cancellationToken));

    /// <summary>Corbeille : salles supprimées de l'école courante.</summary>
    [HttpGet("deleted")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<DeletedRoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDeleted(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDeletedRoomsQuery(), cancellationToken));

    /// <summary>Restaure une salle. 409 ACTIVE_ENTITY_CONFLICT (nom repris) ou PARENT_ENTITY_ARCHIVED (bâtiment supprimé).</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestoreRoomCommand(id), cancellationToken);
        return NoContent();
    }
}
