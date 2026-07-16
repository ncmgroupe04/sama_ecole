using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Commands.CreateMention;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Grades.Queries.GetMentions;
using SamaEcole.Application.Grades.Commands.UpdateGrade;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Tickets JGK-G01/G02 — /grades. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// Deux endpoints distincts pour la saisie, deux permissions distinctes (docs/Volume_7_Security.md
/// « Notes ») : SAISIR une nouvelle note est réservé à l'Enseignant ; CORRIGER une note déjà saisie
/// est ouvert au Directeur ET à l'Enseignant. Un seul endpoint « upsert » aurait mélangé les deux
/// permissions.
/// </summary>
[ApiController]
[Route("api/v1/grades")]
[Authorize]
public class GradesController(ISender mediator) : ControllerBase
{
    public record UpdateGradeRequest(decimal Value, uint RowVersion);
    public record CreateMentionRequest(string Label, decimal MinAverage);

    private const string GradingRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}";

    [HttpPost]
    [Authorize(Roles = nameof(Role.Enseignant))]
    [ProducesResponseType<GradeResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateGradeCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = GradingRoles)]
    [ProducesResponseType<GradeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateGradeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateGradeCommand(id, request.Value, request.RowVersion), cancellationToken));

    /// <summary>
    /// Ticket JGK-G02 — moyennes par matière, moyenne générale pondérée et mention. Recalculé à la
    /// demande, jamais persisté ici (la version figée du bulletin relève de JGK-G03). Lecture ouverte
    /// au Directeur et à l'Enseignant, comme la consultation des notes (Volume_7_Security « Voir »).
    /// </summary>
    [HttpGet("calculate")]
    [Authorize(Roles = GradingRoles)]
    [ProducesResponseType<GradeSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calculate(
        [FromQuery] Guid studentId, [FromQuery] Guid termId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetGradeSummaryQuery(studentId, termId), cancellationToken));

    /// <summary>
    /// Ticket JGK-G02 — mentions personnalisables (Volume 1 §8.4). LECTURE ouverte au Directeur et à
    /// l'Enseignant (l'écran de moyennes en a besoin) ; ÉCRITURE réservée au Directeur
    /// (Volume_7_Security « Paramètres de l'école » : notation, mentions).
    /// </summary>
    [HttpGet("mentions")]
    [Authorize(Roles = GradingRoles)]
    [ProducesResponseType<IReadOnlyList<MentionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMentions(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetMentionsQuery(), cancellationToken));

    [HttpPost("mentions")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<MentionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateMention(
        [FromBody] CreateMentionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CreateMentionCommand(request.Label, request.MinAverage), cancellationToken);

        return CreatedAtAction(nameof(ListMentions), result);
    }
}
