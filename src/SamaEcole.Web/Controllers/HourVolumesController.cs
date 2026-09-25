using SamaEcole.Application.HourVolumes;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Volumes horaires de référence et conformité des emplois du temps (Évolution N°7). Contrôleur mince (règle #8),
/// école lue dans le JWT (règle #10), RLS.
///
/// Lecture : Directeur, Secrétariat (qui tiennent l'emploi du temps). Réglage des volumes : Directeur.
/// </summary>
[ApiController]
[Route("api/v1/hour-volumes")]
[Authorize(Roles = ReadRoles)]
[RequireModule(SchoolModule.Pedagogy)]
public class HourVolumesController(ISender mediator) : ControllerBase
{
    private const string ReadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";
    private const string WriteRole = nameof(Role.Directeur);

    /// <summary>Volumes d'un niveau (et d'une série) : grille de référence, réglages de l'école, volume effectif.</summary>
    [HttpGet("norms")]
    [ProducesResponseType<WeeklyHourNormsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Norms([FromQuery] string gradeLevel, [FromQuery] string? series, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetWeeklyHourNormsQuery(gradeLevel, series), cancellationToken));

    /// <summary>Règle le volume hebdomadaire d'une matière pour un niveau (et une série). 409 si périmé.</summary>
    [HttpPut("norms")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpsertNorm([FromBody] UpsertWeeklyHourNormCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>« Revenir à la référence » : archive le réglage. 409 si périmé.</summary>
    [HttpDelete("norms/{id:guid}")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetNorm(Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResetWeeklyHourNormCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>Conformité des emplois du temps : écarts au volume de référence et chevauchements (une classe ou toutes).</summary>
    [HttpGet("compliance")]
    [ProducesResponseType<TimetableComplianceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Compliance([FromQuery] Guid? classroomId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTimetableComplianceQuery(classroomId), cancellationToken));
}
