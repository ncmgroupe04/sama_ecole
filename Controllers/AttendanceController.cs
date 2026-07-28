using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Attendance.Queries.GetAttendanceSheet;
using SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-D06 — module Présences (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// Deux régimes de permission (feuille de route Présences, docs/Volume_7_Security.md) :
///   * SAISIE (roster + soumission) — Enseignant (borné à ses classes/matières assignées côté
///     handler, via AttendanceScopeAuthorizer), Directeur, Secrétariat.
///   * CONSULTATION (relecture d'une fiche) — Directeur, Secrétariat, Super Admin. Finance EXCLU.
/// </summary>
[ApiController]
[Route("api/v1/attendance")]
[Authorize]
public class AttendanceController(ISender mediator) : ControllerBase
{
    public record SubmitAttendanceRequest(
        Guid ClassroomId, Guid SubjectId, DateOnly Date, string Period, IReadOnlyList<AttendanceEntry> Entries);

    private const string TakeRoles =
        $"{nameof(Role.Enseignant)},{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.Surveillant)}";

    private const string ViewRoles =
        $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.SuperAdmin)},{nameof(Role.Surveillant)}";

    /// <summary>
    /// Grille d'appel d'une classe pour une date/matière/créneau (ticket JGK-D06). La restriction
    /// « l'Enseignant seulement pour ses classes assignées » est appliquée dans le handler (403 sinon).
    /// </summary>
    [HttpGet("roster")]
    [Authorize(Roles = TakeRoles)]
    [ProducesResponseType<AttendanceRosterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Roster(
        [FromQuery] InitializeAttendanceSheetQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>Enregistre l'appel complet (fiche + statut de chaque élève) — ticket JGK-D06.</summary>
    [HttpPost]
    [Authorize(Roles = TakeRoles)]
    [ProducesResponseType<SubmitAttendanceSheetResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitAttendanceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SubmitAttendanceSheetCommand
        {
            ClassroomId = request.ClassroomId,
            SubjectId = request.SubjectId,
            Date = request.Date,
            Period = request.Period,
            Entries = request.Entries
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Relecture d'une fiche d'appel enregistrée (ticket JGK-D06) — consultation, Finance exclu.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType<AttendanceSheetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAttendanceSheetQuery(id), cancellationToken));
}
