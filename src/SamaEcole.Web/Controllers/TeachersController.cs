using SamaEcole.Application.Teachers.Commands.AssignTeacher;
using SamaEcole.Application.Teachers.Commands.CreateTeacher;
using SamaEcole.Application.Teachers.Queries.GetTeacherById;
using SamaEcole.Application.Teachers.Queries.GetTeachers;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-D03 — /teachers (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// Permissions docs/Volume_7_Security.md « Enseignants » : Voir → Super Admin/Directeur/Secrétariat ;
/// Créer/Modifier → Directeur/Secrétariat uniquement (contrairement aux Élèves, Finance et Enseignant
/// n'ont ici aucun accès, même en lecture).
/// </summary>
[ApiController]
[Route("api/v1/teachers")]
[Authorize]
public class TeachersController(ISender mediator) : ControllerBase
{
    public record AssignTeacherRequest(Guid ClassroomId, Guid SubjectId);

    private const string ViewRoles =
        $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    private const string ManageRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    [HttpGet]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType<PaginatedTeachers>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] GetTeachersQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CreateTeacherResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateTeacherCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    /// <summary>
    /// Ticket JGK-D04 — fiche complète : matières qualifiées, attributions classe/matière groupées
    /// par année scolaire (l'historique).
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType<TeacherProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTeacherByIdQuery(id), cancellationToken));

    /// <summary>
    /// Attribution classe/matière sur l'année ACTIVE (ticket JGK-D04) — mêmes permissions que la
    /// création de la fiche (Volume_7_Security.md « Enseignants » : Créer/Modifier).
    /// </summary>
    [HttpPost("{id:guid}/assignments")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<AssignTeacherResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Assign(
        Guid id, [FromBody] AssignTeacherRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new AssignTeacherCommand { TeacherId = id, ClassroomId = request.ClassroomId, SubjectId = request.SubjectId },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, result);
    }
}
