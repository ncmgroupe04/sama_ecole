using SamaEcole.Application.Students.Commands.CreateStudent;
using SamaEcole.Application.Students.Queries.GetStudents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Contrôleur de référence : mince, aucune logique métier, traduit HTTP ↔ MediatR
/// (AGENTS.md règle #8). Voir openapi.yaml pour le contrat complet, ticket JGK-D01.
/// </summary>
[ApiController]
[Route("api/v1/students")]
[Authorize]
public class StudentsController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// La requête est liée depuis la chaîne de requête. Elle ne porte PAS de SchoolId : l'école est
    /// lue dans le JWT (AGENTS.md règle #10) — l'accepter du client permettrait de lire les élèves
    /// d'un autre établissement en changeant un paramètre d'URL.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginatedStudents>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] GetStudentsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreateStudentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateStudentCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }
}
