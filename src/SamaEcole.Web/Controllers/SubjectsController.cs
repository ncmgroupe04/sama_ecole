using SamaEcole.Application.Subjects.Commands.CreateSubject;
using SamaEcole.Application.Subjects.Commands.DeleteSubject;
using SamaEcole.Application.Subjects.Commands.UpdateSubject;
using SamaEcole.Application.Subjects.Queries.GetSubjects;
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
public class SubjectsController(ISender mediator) : ControllerBase
{
    public record UpdateSubjectRequest(string Name, string Level, decimal Coefficient, uint RowVersion);

    /// <summary>
    /// LECTURE ouverte à tout utilisateur de l'école : l'enseignant a besoin des coefficients pour
    /// comprendre ses moyennes, le secrétariat pour composer les bulletins. La réserver au Directeur
    /// n'apporterait rien.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SubjectDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSubjectsQuery(), cancellationToken));

    /// <summary>
    /// ÉCRITURE : Directeur toujours ; Secrétariat seulement si SON école a activé la délégation
    /// (GradingPolicies.CanManageGradingScale, ticket JGK-G02, docs/Volume_7_Security.md « Paramètres
    /// de l'école »). Jamais l'Enseignant ni la Finance.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = GradingPolicies.CanManageGradingScale)]
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
    [Authorize(Policy = GradingPolicies.CanManageGradingScale)]
    [ProducesResponseType<UpdateSubjectResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateSubjectRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateSubjectCommand(id, request.Name, request.Level, request.Coefficient, request.RowVersion),
            cancellationToken));

    /// <summary>
    /// Archive (soft delete) une matière créée par erreur. Refusée en 409 si des notes existent déjà
    /// pour elle (DeleteSubjectCommandHandler). `rowVersion` en query string, comme DELETE /grades/{id}.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = GradingPolicies.CanManageGradingScale)]
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
