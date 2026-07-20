using SamaEcole.Application.Students.Commands.CreateStudent;
using SamaEcole.Application.Students.Commands.DeleteStudent;
using SamaEcole.Application.Students.Commands.UpdateStudent;
using SamaEcole.Application.Students.Queries.GetStudentDetail;
using SamaEcole.Application.Students.Queries.GetStudents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Contrôleur de référence : mince, aucune logique métier, traduit HTTP ↔ MediatR
/// (AGENTS.md règle #8). Voir openapi.yaml pour le contrat complet, ticket JGK-D01.
///
/// LECTURE et CRÉATION ouvertes à tout utilisateur authentifié (comportement historique inchangé).
/// CORRIGER ou ARCHIVER une fiche déjà créée est réservé au Directeur et au Secrétariat.
/// </summary>
[ApiController]
[Route("api/v1/students")]
[Authorize]
public class StudentsController(ISender mediator) : ControllerBase
{
    public record UpdateStudentRequest(
        string FullName,
        DateOnly BirthDate,
        string BirthPlace,
        string Gender,
        Guid ClassroomId,
        string? PhotoUrl,
        string? GuardianName,
        string? GuardianPhone,
        uint RowVersion);

    private const string ManageRoles = "Directeur,Secretariat";

    /// <summary>
    /// La requête est liée depuis la chaîne de requête. Elle ne porte PAS de SchoolId : l'école est
    /// lue dans le JWT (AGENTS.md règle #10) — l'accepter du client permettrait de lire les élèves
    /// d'un autre établissement en changeant un paramètre d'URL.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginatedStudents>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] GetStudentsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>
    /// Fiche élève complète (ticket JGK-D02) : identité, historique scolaire, notes par trimestre et
    /// historique des paiements. Comme pour la liste, l'école vient du JWT (règle #10) : un id d'une
    /// autre école est introuvable (404), jamais servi. Les sections vides reviennent en listes vides
    /// pour laisser l'UI afficher un empty-state.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StudentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentDetailQuery(id), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreateStudentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateStudentCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    /// <summary>
    /// Corrige les informations non financières et non sécurisées d'une fiche élève déjà créée
    /// (état civil, classe, coordonnées du tuteur). Le matricule n'est jamais modifiable (règle #3).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UpdateStudentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateStudentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateStudentCommand(
                id, request.FullName, request.BirthDate, request.BirthPlace, request.Gender,
                request.ClassroomId, request.PhotoUrl, request.GuardianName, request.GuardianPhone,
                request.RowVersion),
            cancellationToken));

    /// <summary>
    /// Archive (soft delete) une fiche élève créée par pure erreur de saisie. Refusée en 409 dès
    /// qu'une inscription ou une note existe déjà (DeleteStudentCommandHandler). `rowVersion` en
    /// query string, comme DELETE /grades/{id}.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteStudentCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }
}
