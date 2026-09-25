using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.ClassSubjects.Commands;
using SamaEcole.Application.ClassSubjects.Queries;
using SamaEcole.Application.Exemptions;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Programme des classes et matières optionnelles (Évolution N°6 — séries du Baccalauréat sénégalais).
/// Contrôleur mince : aucune logique métier ici (règle #8). L'école vient du JWT, l'année de l'année ACTIVE
/// résolue serveur (règle #10).
///
/// PROGRAMME d'une classe : lecture Directeur + Secrétariat, écriture Directeur seul — comme les coefficients
/// (arbitrage A9), il pilote les bulletins. OPTIONS d'un élève : Directeur + Secrétariat, qui les saisit à
/// l'inscription. Les coefficients eux-mêmes restent sur /api/v1/coefficients.
/// </summary>
[ApiController]
[Route("api/v1/class-subjects")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class ClassSubjectsController(ISender mediator) : ControllerBase
{
    private const string StaffRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";
    private const string WriteRole = nameof(Role.Directeur);

    /// <summary>Programme d'une classe : matières, coefficient effectif, groupes d'options, valeur officielle.</summary>
    [HttpGet]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<ClassSubjectsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromQuery] Guid classroomId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassSubjectsQuery(classroomId), cancellationToken));

    /// <summary>Ajoute une matière propre à l'établissement au programme d'une classe.</summary>
    [HttpPost]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Add([FromBody] AddClassSubjectCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return Created($"/api/v1/class-subjects?classroomId={command.ClassroomId}", new { id });
    }

    /// <summary>Active/désactive une matière et règle son groupe d'options. 409 si la ligne a changé entre-temps.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateClassSubjectCommand command, CancellationToken cancellationToken)
    {
        var rowVersion = await mediator.Send(command with { Id = id }, cancellationToken);
        return Ok(new { id, rowVersion });
    }

    /// <summary>« Réinitialiser aux coefficients officiels du Sénégal » : programme et coefficients du modèle de la série.</summary>
    [HttpPost("reset")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType<ClassTemplateReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reset([FromBody] ResetClassSubjectsCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>Donne l'option par défaut aux élèves de la classe qui n'en ont pas encore (année active).</summary>
    [HttpPost("assign-default-options")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<AssignDefaultOptionsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignDefaultOptions(
        [FromBody] AssignDefaultOptionsCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>Groupes d'options d'une classe (inscription) ou de la classe d'un élève avec ses choix (fiche élève).</summary>
    [HttpGet("options")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<SubjectOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Options(
        [FromQuery] Guid? classroomId, [FromQuery] Guid? studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSubjectOptionsQuery(classroomId, studentId), cancellationToken));

    /// <summary>Enregistre les options d'un élève pour l'année active (liste complète des choix).</summary>
    [HttpPut("students/{studentId:guid}/options")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<IReadOnlyList<Guid>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetStudentOptions(
        Guid studentId, [FromBody] SetStudentSubjectOptionsCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command with { StudentId = studentId }, cancellationToken));

    /// <summary>
    /// Matières dispensables d'un élève et ses dispenses de l'année active, motif compris. Réservé au Directeur et au
    /// Secrétariat : le motif peut être médical.
    /// </summary>
    [HttpGet("students/{studentId:guid}/exemptions")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<StudentExemptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetStudentExemptions(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentExemptionsQuery(studentId), cancellationToken));

    public record SetStudentExemptionsRequest(IReadOnlyList<SubjectExemptionInput>? Exemptions);

    /// <summary>Remplace les dispenses de l'élève pour l'année active (liste vide = aucune). Motif obligatoire.</summary>
    [HttpPut("students/{studentId:guid}/exemptions")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetStudentExemptions(
        Guid studentId, [FromBody] SetStudentExemptionsRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetStudentExemptionsCommand(studentId, request.Exemptions ?? []), cancellationToken);
        return NoContent();
    }
}
