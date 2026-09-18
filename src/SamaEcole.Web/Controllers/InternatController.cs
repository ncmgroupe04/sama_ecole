using SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;
using SamaEcole.Application.Internat.Queries.GetInternatDashboard;
using SamaEcole.Application.Internat.Queries.SearchBoardableStudents;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Internat (spec docs/superpowers/specs/2026-09-18-module-internat-design.md). Contrôleur
/// mince, aucune logique métier ici (AGENTS.md règle #8). Verrouillé par [RequireModule] — un
/// Directeur qui n'a pas activé l'Internat reçoit 403 MODULE_DISABLED sur toutes les routes
/// ci-dessous, y compris la lecture (spec §4 : le module est désactivé par défaut, contrairement à
/// Pédagogie/Finance).
/// </summary>
[ApiController]
[Route("api/v1/internat")]
[Authorize]
[RequireModule(SchoolModule.Internat)]
public class InternatController(ISender mediator) : ControllerBase
{
    // Directeur + Secretariat + Surveillant — décision actée en brainstorming (spec §4), même trio
    // que ParentSummonsController pour la Vie Scolaire.
    private const string ManageRoles = "Directeur,Secretariat,Surveillant";

    [HttpGet("dashboard")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<InternatDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetInternatDashboardQuery(), cancellationToken));

    [HttpGet("students/search")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<BoardableStudentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchStudents([FromQuery] string term, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SearchBoardableStudentsQuery(term), cancellationToken));

    public record ChangeBoardingAssignmentRequest(Guid? RoomId, BoardingStatus BoardingStatus, bool IncludeBoardingFee, uint RowVersion);

    [HttpPost("assignments/{enrollmentId:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<EnrollmentBoardingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeAssignment(
        Guid enrollmentId, [FromBody] ChangeBoardingAssignmentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ChangeBoardingAssignmentCommand(
                enrollmentId, request.RoomId, request.BoardingStatus, request.IncludeBoardingFee, request.RowVersion),
            cancellationToken));
}
