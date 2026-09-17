using SamaEcole.Application.Subjects.Commands.CreateSubject;
using SamaEcole.Application.Subjects.Commands.DeleteSubject;
using SamaEcole.Application.Subjects.Commands.UpdateSubject;
using SamaEcole.Application.Subjects.Queries.GetSubjects;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-C03 — /subjects (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
/// </summary>
[ApiController]
[Route("api/v1/subjects")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class SubjectsController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Les champs de structure sont optionnels et à leur valeur neutre par défaut : un client qui ne les
    /// envoie pas (l'écran des matières avant la configuration APC, un script existant) modifie la
    /// matière exactement comme avant.
    /// </summary>
    public record UpdateSubjectRequest(
        string Name,
        string Level,
        decimal Coefficient,
        uint RowVersion,
        Guid? ParentSubjectId = null,
        decimal? MaxScore = null,
        int DisplayOrder = 0,
        string? Column1Header = null,
        string? Column2Header = null);

    /// <summary>
    /// ÉCRITURE : Directeur, Secrétariat et Enseignant — accès inconditionnel, sans le garde-fou par
    /// école de GradingPolicies.CanManageGradingScale (qui reste réservé au barème et aux mentions,
    /// GradesController/SchoolSettingsController). Volontairement un rôle dédié plutôt que cette policy
    /// partagée : l'élargir aurait aussi ouvert le barème et les mentions à l'Enseignant, jamais demandé.
    /// Jamais la Finance.
    /// </summary>
    private const string ManageRoles = "Directeur,Secretariat,Enseignant";

    /// <summary>LECTURE ouverte à tout utilisateur de l'école.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SubjectDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSubjectsQuery(), cancellationToken));

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<SubjectResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSubjectCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    /// <summary>Corrige le libellé/niveau/coefficient d'une matière déjà créée. Même permission que Create.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UpdateSubjectResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateSubjectRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateSubjectCommand(
                id, request.Name, request.Level, request.Coefficient, request.RowVersion,
                request.ParentSubjectId, request.MaxScore, request.DisplayOrder,
                request.Column1Header, request.Column2Header),
            cancellationToken));

    /// <summary>
    /// Archive (soft delete) une matière créée par erreur. Refusée en 409 si des notes existent déjà
    /// pour elle (DeleteSubjectCommandHandler). `rowVersion` en query string, comme DELETE /grades/{id}.
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
        await mediator.Send(new DeleteSubjectCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }
}
