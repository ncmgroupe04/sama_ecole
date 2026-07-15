using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;
using SamaEcole.Application.Schools.Queries.GetSchoolSettings;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-B02 — paramètres de l'établissement COURANT (openapi.yaml /schools/current/settings).
///
/// « current » vient du claim JWT, jamais d'un paramètre de route : aucun Directeur ne peut désigner
/// l'établissement d'un autre (AGENTS.md règle #10).
/// </summary>
[ApiController]
[Route("api/v1/schools/current/settings")]
[Authorize]
public class SchoolSettingsController(ISender mediator) : ControllerBase
{
    public record UpdateSettingsRequest(
        string GradingScale,
        string StudentMatriculeFormat,
        string TeacherMatriculeFormat,
        int AutoLogoutMinutes,
        string DateFormat,
        int TuitionMonthsPerYear);

    /// <summary>
    /// LECTURE ouverte à tout utilisateur de l'école : le format de date et le barème pilotent
    /// l'affichage de TOUS les écrans (Secrétariat, Finance, Enseignant). Les réserver au Directeur
    /// obligerait chaque autre rôle à afficher des dates au mauvais format.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SchoolSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSchoolSettingsQuery(), cancellationToken));

    /// <summary>ÉCRITURE réservée au Directeur (critère du ticket JGK-B02).</summary>
    [HttpPut]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new UpdateSchoolSettingsCommand(
                request.GradingScale,
                request.StudentMatriculeFormat,
                request.TeacherMatriculeFormat,
                request.AutoLogoutMinutes,
                request.DateFormat,
                request.TuitionMonthsPerYear),
            cancellationToken);

        return Ok(result);
    }
}
