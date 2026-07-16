using SamaEcole.Application.Grades.Commands.SaveGrade;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-G01 — /grades. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// ÉCRITURE réservée au Directeur et à l'Enseignant : c'est l'enseignant qui note, le Directeur pouvant
/// corriger/superviser. Secrétariat et Finance n'ont pas leur place dans la notation.
/// </summary>
[ApiController]
[Route("api/v1/grades")]
[Authorize]
public class GradesController(ISender mediator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}")]
    [ProducesResponseType<SaveGradeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Save([FromBody] SaveGradeCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));
}
