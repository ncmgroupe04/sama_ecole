using SamaEcole.Application.Reports.Queries.GetAttendanceExport;
using SamaEcole.Application.Reports.Queries.GetAttendanceReport;
using SamaEcole.Application.Reports.Queries.GetDirectorDashboard;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-R01 — /reports (openapi.yaml). Contrôleur mince : aucune logique métier ici (AGENTS.md
/// règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// Tableau de bord analytique du Directeur : agrège plusieurs modules (effectifs, RH, présence,
/// abonnement). Réservé au Directeur et au Super Admin — c'est une vue de PILOTAGE de l'établissement,
/// plus large que le tableau de bord financier (Directeur+Finance) de JGK-F04.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize]
public class ReportsController(ISender mediator) : ControllerBase
{
    [HttpGet("dashboard")]
    [Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.SuperAdmin)}")]
    [ProducesResponseType<DirectorDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDirectorDashboardQuery(), cancellationToken));

    /// <summary>
    /// Ticket JGK-R02 — rapport d'assiduité détaillé par classe et par élève sur une période. Ouvert au
    /// Directeur, au Secrétariat et au Super Admin (Volume 7 « Présences » : Finance et Enseignant exclus
    /// de la consultation transverse). Le classId éventuel est validé serveur comme appartenant au tenant.
    /// </summary>
    [HttpGet("attendance")]
    [Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.SuperAdmin)}")]
    [ProducesResponseType<AttendanceReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Attendance(
        [FromQuery] GetAttendanceReportQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>
    /// Ticket JGK-R03 — export du rapport d'assiduité en PDF ou CSV. Mêmes filtres/validation que R02,
    /// mêmes permissions (Directeur/Secrétariat/Super Admin). Renvoie un flux de fichier nommé
    /// dynamiquement, avec le type MIME adéquat. Les données transitent sous la RLS de l'école.
    /// </summary>
    [HttpGet("attendance/export")]
    [Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.SuperAdmin)}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ExportAttendance(
        [FromQuery] GetAttendanceExportQuery query, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(query, cancellationToken);

        // FileStreamResult : le contenu est déjà en mémoire, on l'enveloppe dans un flux pour un
        // téléchargement nommé avec le bon type MIME (application/pdf ou text/csv).
        return File(new MemoryStream(result.Content), result.ContentType, result.FileName);
    }
}
