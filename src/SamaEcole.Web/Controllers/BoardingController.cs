using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Beds.ChangeBedStatus;
using SamaEcole.Application.Boarding.Beds.CreateBed;
using SamaEcole.Application.Boarding.Beds.DeleteBed;
using SamaEcole.Application.Boarding.Beds.GetDeletedBeds;
using SamaEcole.Application.Boarding.Beds.RestoreBed;
using SamaEcole.Application.Boarding.Dormitories.CreateDormitory;
using SamaEcole.Application.Boarding.Dormitories.DeleteDormitory;
using SamaEcole.Application.Boarding.Dormitories.GetDeletedDormitories;
using SamaEcole.Application.Boarding.Dormitories.GetDormitory;
using SamaEcole.Application.Boarding.Dormitories.ListDormitories;
using SamaEcole.Application.Boarding.Dormitories.RestoreDormitory;
using SamaEcole.Application.Boarding.Dormitories.UpdateDormitory;
using SamaEcole.Application.Boarding.Rooms.CreateDormitoryRoom;
using SamaEcole.Application.Boarding.Rooms.DeleteDormitoryRoom;
using SamaEcole.Application.Boarding.Rooms.GetDeletedDormitoryRooms;
using SamaEcole.Application.Boarding.Rooms.RestoreDormitoryRoom;
using SamaEcole.Application.Boarding.Rooms.UpdateDormitoryRoom;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Internat, modèle Pavillon/Lit (spec docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md
/// §6). Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8). Verrouillé par [RequireModule] — 403
/// MODULE_DISABLED si l'école n'a pas activé l'Internat, lecture comprise.
///
/// Routes PLATES (/dormitories, /rooms, /beds), comme /buildings et /rooms du module Infrastructures : pas
/// d'imbrication d'URL. Lecture : Directeur, Secretariat, Surveillant. Écriture : Directeur, Secretariat (spec §5.1).
/// </summary>
[ApiController]
[Route("api/v1/boarding")]
[Authorize]
[RequireModule(SchoolModule.Internat)]
public class BoardingController(ISender mediator) : ControllerBase
{
    private const string ReadRoles = "Directeur,Secretariat,Surveillant";
    private const string ManageRoles = "Directeur,Secretariat";

    public record CreateDormitoryRequest(
        string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
        Guid? SupervisorUserId, string? Notes);

    public record UpdateDormitoryRequest(
        string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
        Guid? SupervisorUserId, string? Notes, uint RowVersion);

    public record CreateRoomRequest(Guid DormitoryId, string Name, int BedCount);

    public record UpdateRoomRequest(string Name, uint RowVersion);

    public record CreateBedRequest(Guid DormitoryRoomId, int? BedNumber);

    public record ChangeBedStatusRequest(BedStatus Status, uint RowVersion);

    // ------------------------------------------------------------------ Pavillons

    [HttpGet("dormitories")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<DormitorySummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDormitories([FromQuery] DormitoryGender? gender, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListDormitoriesQuery(gender), cancellationToken));

    [HttpGet("dormitories/{id:guid}")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<DormitoryDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDormitory(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDormitoryQuery(id), cancellationToken));

    [HttpPost("dormitories")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DormitoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateDormitory(
        [FromBody] CreateDormitoryRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateDormitoryCommand
            {
                Name = request.Name,
                Gender = request.Gender,
                SupervisorName = request.SupervisorName,
                SupervisorPhone = request.SupervisorPhone,
                SupervisorUserId = request.SupervisorUserId,
                Notes = request.Notes
            }, cancellationToken);

        return CreatedAtAction(nameof(GetDormitory), new { id = result.Id }, result);
    }

    [HttpPut("dormitories/{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DormitoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateDormitory(
        Guid id, [FromBody] UpdateDormitoryRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateDormitoryCommand(
                id, request.Name, request.Gender, request.SupervisorName, request.SupervisorPhone,
                request.SupervisorUserId, request.Notes, request.RowVersion),
            cancellationToken));

    [HttpDelete("dormitories/{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDormitory(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteDormitoryCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>Corbeille : pavillons supprimés de l'école courante.</summary>
    [HttpGet("dormitories/deleted")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<DeletedBoardingItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDeletedDormitories(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDeletedDormitoriesQuery(), cancellationToken));

    /// <summary>Restaure un pavillon. 409 ACTIVE_ENTITY_CONFLICT si son nom est repris par un pavillon actif.</summary>
    [HttpPost("dormitories/{id:guid}/restore")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreDormitory(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestoreDormitoryCommand(id), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Chambres

    /// <summary>Crée une chambre ET ses lits 1..BedCount (tout ou rien).</summary>
    [HttpPost("rooms")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DormitoryRoomResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateRoom(
        [FromBody] CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateDormitoryRoomCommand { DormitoryId = request.DormitoryId, Name = request.Name, BedCount = request.BedCount },
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("rooms/{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DormitoryRoomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateRoom(
        Guid id, [FromBody] UpdateRoomRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateDormitoryRoomCommand(id, request.Name, request.RowVersion), cancellationToken));

    [HttpDelete("rooms/{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRoom(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteDormitoryRoomCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>Corbeille : chambres supprimées de l'école courante.</summary>
    [HttpGet("rooms/deleted")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<DeletedBoardingItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDeletedRooms(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDeletedDormitoryRoomsQuery(), cancellationToken));

    /// <summary>Restaure une chambre. 409 ACTIVE_ENTITY_CONFLICT (nom repris) ou PARENT_ENTITY_ARCHIVED (pavillon supprimé).</summary>
    [HttpPost("rooms/{id:guid}/restore")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreRoom(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestoreDormitoryRoomCommand(id), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Lits

    [HttpPost("beds")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<BedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateBed(
        [FromBody] CreateBedRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateBedCommand { DormitoryRoomId = request.DormitoryRoomId, BedNumber = request.BedNumber },
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Bascule un lit entre Available et Maintenance. 409 RESOURCE_IN_USE si le lit est occupé.</summary>
    [HttpPut("beds/{id:guid}/status")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<BedDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeBedStatus(
        Guid id, [FromBody] ChangeBedStatusRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ChangeBedStatusCommand(id, request.Status, request.RowVersion), cancellationToken));

    [HttpDelete("beds/{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteBed(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteBedCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>Corbeille : lits supprimés de l'école courante.</summary>
    [HttpGet("beds/deleted")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<DeletedBoardingItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDeletedBeds(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDeletedBedsQuery(), cancellationToken));

    /// <summary>Restaure un lit. 409 ACTIVE_ENTITY_CONFLICT (numéro repris) ou PARENT_ENTITY_ARCHIVED (chambre supprimée).</summary>
    [HttpPost("beds/{id:guid}/restore")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreBed(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestoreBedCommand(id), cancellationToken);
        return NoContent();
    }
}
