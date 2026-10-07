using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Attendance;
using SamaEcole.Application.Boarding.Attendance.GetAttendanceSheet;
using SamaEcole.Application.Boarding.Attendance.RecordAttendance;
using SamaEcole.Application.Boarding.Beds.ChangeBedStatus;
using SamaEcole.Application.Boarding.Beds.CreateBed;
using SamaEcole.Application.Boarding.Beds.DeleteBed;
using SamaEcole.Application.Boarding.Beds.GetDeletedBeds;
using SamaEcole.Application.Boarding.Beds.RestoreBed;
using SamaEcole.Application.Boarding.Boarders.AssignBed;
using SamaEcole.Application.Boarding.Boarders.EndBoarding;
using SamaEcole.Application.Boarding.Boarders.GetBoarder;
using SamaEcole.Application.Boarding.Boarders.ListBoarders;
using SamaEcole.Application.Boarding.Boarders.UpdateBoarderProfile;
using SamaEcole.Application.Boarding.Dormitories.CreateDormitory;
using SamaEcole.Application.Boarding.Dormitories.DeleteDormitory;
using SamaEcole.Application.Boarding.Dormitories.GetDeletedDormitories;
using SamaEcole.Application.Boarding.Dormitories.GetDormitory;
using SamaEcole.Application.Boarding.Dormitories.ListDormitories;
using SamaEcole.Application.Boarding.Dormitories.RestoreDormitory;
using SamaEcole.Application.Boarding.Documents;
using SamaEcole.Application.Boarding.Documents.BoarderSheet;
using SamaEcole.Application.Boarding.Documents.DormitoryRegister;
using SamaEcole.Application.Boarding.Dormitories.UpdateDormitory;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Application.Boarding.Leaves.DeclareLeave;
using SamaEcole.Application.Boarding.Leaves.ListLeaves;
using SamaEcole.Application.Boarding.Leaves.RecordReturn;
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

    public record AssignBedRequest(Guid EnrollmentId, BoardingRegime Regime, Guid? BedId, bool IncludeBoardingFee, uint? RowVersion);

    public record UpdateBoarderProfileRequest(
        string? MedicalNotes, string? EmergencyContactName, string? EmergencyContactPhone,
        List<AllowedExitPersonDto>? AllowedExitPersons, uint RowVersion);

    public record DeclareLeaveRequest(
        Guid BoarderId, DateOnly LeaveDate, DateOnly ExpectedReturnDate, BoardingLeaveReason Reason,
        string? ReasonDetail, string AccompaniedBy);

    public record ReturnLeaveRequest(uint RowVersion, DateOnly? ActualReturnDate);

    public record RecordAttendanceRequest(DateOnly Date, Guid DormitoryId, List<AttendanceEntryInput>? Entries);

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

    // ------------------------------------------------------------------ Pensionnaires

    /// <summary>
    /// Affecte un élève (inscription de l'année active) à un lit, ou l'inscrit en demi-pension. Un séjour actif existant est
    /// TRANSFÉRÉ : le <c>rowVersion</c> du séjour est alors requis. 409 <c>BED_UNAVAILABLE</c> si le lit est pris.
    /// </summary>
    [HttpPost("assign-bed")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<BoarderListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignBed([FromBody] AssignBedRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new AssignBedCommand(request.EnrollmentId, request.Regime, request.BedId, request.IncludeBoardingFee, request.RowVersion),
            cancellationToken));

    /// <summary>Met fin au séjour (lit libéré, rien n'est supprimé). 409 <c>LEAVE_IN_PROGRESS</c> si une sortie est ouverte.</summary>
    [HttpDelete("unassign-bed/{boarderId:guid}")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnassignBed(
        Guid boarderId, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new EndBoardingCommand(boarderId, rowVersion), cancellationToken);
        return NoContent();
    }

    [HttpGet("boarders")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<PaginatedBoarders>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ListBoarders(
        [FromQuery] Guid? dormitoryId, [FromQuery] Guid? roomId, [FromQuery] BoardingRegime? regime,
        [FromQuery] string? status, [FromQuery] string? search,
        CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await mediator.Send(
            new ListBoardersQuery
            {
                DormitoryId = dormitoryId, RoomId = roomId, Regime = regime, Status = status ?? "active",
                Search = search, Page = page, PageSize = pageSize
            }, cancellationToken));

    /// <summary>Fiche complète. <c>medicalNotes</c> est <c>null</c> pour tout rôle autre que Directeur et Surveillant.</summary>
    [HttpGet("boarders/{id:guid}")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<BoarderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoarder(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetBoarderQuery(id), cancellationToken));

    /// <summary>
    /// Fiche médicale, contact d'urgence et personnes habilitées. Le Secrétariat reçoit 403 s'il envoie
    /// <c>medicalNotes</c> ; en l'omettant, la fiche médicale existante est conservée.
    /// </summary>
    [HttpPut("boarders/{id:guid}/profile")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<BoarderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateBoarderProfile(
        Guid id, [FromBody] UpdateBoarderProfileRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateBoarderProfileCommand(
                id, request.MedicalNotes, request.EmergencyContactName, request.EmergencyContactPhone,
                request.AllowedExitPersons ?? [], request.RowVersion),
            cancellationToken));

    // ------------------------------------------------------------------ Sorties

    /// <summary>
    /// Déclare la sortie d'un pensionnaire actif. Un accompagnateur hors liste est ACCEPTÉ et signalé
    /// (<c>isCompanionUnlisted</c>). 409 <c>LEAVE_ALREADY_OPEN</c> si une sortie est déjà ouverte.
    /// </summary>
    [HttpPost("leaves")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<BoardingLeaveDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeclareLeave([FromBody] DeclareLeaveRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new DeclareLeaveCommand(
                request.BoarderId, request.LeaveDate, request.ExpectedReturnDate, request.Reason, request.ReasonDetail,
                request.AccompaniedBy),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Enregistre le retour (date du jour par défaut). 409 <c>LEAVE_ALREADY_RETURNED</c> s'il est déjà enregistré.</summary>
    [HttpPut("leaves/{id:guid}/return")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<BoardingLeaveDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordReturn(
        Guid id, [FromBody] ReturnLeaveRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new RecordReturnCommand(id, request.RowVersion, request.ActualReturnDate), cancellationToken));

    /// <summary>Registre des sorties, plus récentes d'abord. <c>status</c> : Pending, Active, Overdue ou Returned.</summary>
    [HttpGet("leaves")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<PaginatedLeaves>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ListLeaves(
        [FromQuery] Guid? boarderId, [FromQuery] Guid? dormitoryId, [FromQuery] LeaveStatus? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool unlistedOnly,
        CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await mediator.Send(
            new ListLeavesQuery
            {
                BoarderId = boarderId, DormitoryId = dormitoryId, Status = status, From = from, To = to,
                UnlistedOnly = unlistedOnly, Page = page, PageSize = pageSize
            }, cancellationToken));

    // ------------------------------------------------------------------ Pointage de nuit

    /// <summary>
    /// Pointage collectif d'un pavillon. Réponse LIGNE PAR LIGNE : une ligne refusée n'empêche pas les autres. Un pensionnaire
    /// en sortie n'est accepté qu'absent.
    /// </summary>
    [HttpPost("attendance")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<RecordAttendanceResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordAttendance(
        [FromBody] RecordAttendanceRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new RecordAttendanceCommand(request.Date, request.DormitoryId, request.Entries ?? []), cancellationToken));

    /// <summary>Feuille d'appel du soir d'un pavillon (absents en permission pré-remplis).</summary>
    [HttpGet("attendance")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<AttendanceSheetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetAttendanceSheet(
        [FromQuery] Guid dormitoryId, [FromQuery] DateOnly date, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAttendanceSheetQuery(dormitoryId, date), cancellationToken));

    // ------------------------------------------------------------------ Documents PDF

    /// <summary>
    /// Fiche individuelle d'un pensionnaire (séjour actif ou clos). La fiche médicale n'y figure que pour le Directeur et le
    /// Surveillant ; tout autre rôle reçoit le même document sans ce bloc. L'impression est tracée dans le journal d'audit.
    /// </summary>
    [HttpGet("boarders/{id:guid}/pdf")]
    [Authorize(Roles = ReadRoles)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoarderSheetPdf(Guid id, CancellationToken cancellationToken)
        => PdfFile(await mediator.Send(new GetBoarderSheetPdfQuery(id), cancellationToken), "Fiche-Pensionnaire");

    /// <summary>
    /// Registre imprimable d'un pavillon pour une nuit (<c>date</c>, aujourd'hui par défaut). Sans donnée de santé ni
    /// téléphone. 422 pour une date future ou hors de l'année active. L'impression est tracée dans le journal d'audit.
    /// </summary>
    [HttpGet("dormitories/{id:guid}/register-pdf")]
    [Authorize(Roles = ReadRoles)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetDormitoryRegisterPdf(
        Guid id, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
        => PdfFile(await mediator.Send(new GetDormitoryRegisterPdfQuery(id, date), cancellationToken), "Registre-Pavillon");

    /// <summary>Nom de fichier assaini : ASCII (lettres, chiffres, tiret, souligné), les espaces deviennent des tirets.</summary>
    private IActionResult PdfFile(BoardingPdfResult result, string prefix)
    {
        var safe = new string(result.FileStem
            .Select(ch => ch == ' ' ? '-' : ch)
            .Where(ch => ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_')
            .ToArray());

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{prefix}-{safe}.pdf\"";
        return File(result.Content, "application/pdf");
    }
}
