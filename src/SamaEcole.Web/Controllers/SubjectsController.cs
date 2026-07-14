using SamaEcole.Application.Subjects.Commands.CreateSubject;
using SamaEcole.Application.Subjects.Queries.GetSubjects;
using SamaEcole.Domain.Enums;
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
    /// ÉCRITURE réservée au Directeur : le coefficient relève de la NOTATION, rangée parmi les
    /// paramètres de l'établissement que seul le Directeur fixe (docs/Volume_7_Security.md §15).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = nameof(Role.Directeur))]
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
}
