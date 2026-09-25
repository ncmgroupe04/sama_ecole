using SamaEcole.Application.Syllabus;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Programmes nationaux et suivi de leur avancement (Évolution N°7). Contrôleur mince (règle #8), école lue dans le JWT
/// (règle #10), RLS.
///
/// Lecture du programme : Directeur, Secrétariat, Enseignant (qui le pointe au cahier de texte), Surveillant (qui lit le
/// cahier de texte). Écriture du programme :
/// Directeur. Tableau d'avancement : Directeur, Secrétariat.
/// </summary>
[ApiController]
[Route("api/v1/syllabus")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class SyllabusController(ISender mediator) : ControllerBase
{
    private const string ReadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.Enseignant)},{nameof(Role.Surveillant)}";
    private const string DashboardRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";
    private const string WriteRole = nameof(Role.Directeur);

    /// <summary>Programme d'une matière pour un niveau (ou pour le niveau d'une classe).</summary>
    [HttpGet("units")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<SyllabusUnitsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Units(
        [FromQuery] Guid subjectId, [FromQuery] string? gradeLevel, [FromQuery] Guid? classroomId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSyllabusUnitsQuery(subjectId, gradeLevel, classroomId), cancellationToken));

    /// <summary>Ajoute des chapitres (un par ligne) au programme d'une matière pour un niveau.</summary>
    [HttpPost("units")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddUnits([FromBody] AddSyllabusUnitsCommand command, CancellationToken cancellationToken)
        => Ok(new { added = await mediator.Send(command, cancellationToken) });

    /// <summary>Importe la trame nationale codée d'une matière pour un niveau (422 s'il n'en existe pas).</summary>
    [HttpPost("import-template")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportTemplate([FromBody] ImportSyllabusTemplateCommand command, CancellationToken cancellationToken)
        => Ok(new { added = await mediator.Send(command, cancellationToken) });

    /// <summary>Corrige un chapitre. 409 si la ligne a changé entre-temps.</summary>
    [HttpPut("units/{id:guid}")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUnit(Guid id, [FromBody] UpdateSyllabusUnitCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command with { Id = id }, cancellationToken);
        return NoContent();
    }

    /// <summary>Archive un chapitre (suppression logique). 409 si la ligne a changé entre-temps.</summary>
    [HttpDelete("units/{id:guid}")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUnit(Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteSyllabusUnitCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>Tableau de bord : avancement du programme par classe, matière et enseignant (année active).</summary>
    [HttpGet("coverage")]
    [Authorize(Roles = DashboardRoles)]
    [ProducesResponseType<SyllabusCoverageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Coverage(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSyllabusCoverageQuery(), cancellationToken));
}
