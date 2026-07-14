using SamaEcole.Application.Students.Commands.CreateStudent;
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
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStudentCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }
}
