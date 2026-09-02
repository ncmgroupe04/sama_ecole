using SamaEcole.Application.VieScolaire.Commands.CloseParentSummons;
using SamaEcole.Application.VieScolaire.Commands.CreateParentSummons;
using SamaEcole.Application.VieScolaire.Queries.GetParentNotice;
using SamaEcole.Application.VieScolaire.Queries.GetParentNoticePdf;
using SamaEcole.Application.VieScolaire.Queries.GetParentSummons;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Convocations de parent/tuteur (module Vie Scolaire) — motif libre, distinct d'une sanction
/// disciplinaire (voir DisciplineController). Mêmes rôles que la Discipline et la Surveillance.
/// </summary>
[ApiController]
[Route("api/v1/parent-summons")]
[Authorize(Roles = $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Surveillant)}")]
public class ParentSummonsController(ISender mediator, ILogger<ParentSummonsController> logger) : ControllerBase
{
    /// <summary>Corps du PATCH de suite : l'identifiant vient de la route, jamais du corps.</summary>
    public record CloseParentSummonsOutcomeRequest(ParentSummonsStatus Outcome, string? OutcomeNotes);

    [HttpGet]
    [ProducesResponseType<List<ParentSummonsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ParentSummonsDto>>> GetParentSummons(CancellationToken cancellationToken)
        => await mediator.Send(new GetParentSummonsQuery(), cancellationToken);

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateParentSummons(CreateParentSummonsCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetNotice), new { id }, id);
    }

    /// <summary>
    /// Consigne la suite de l'entretien (honorée, non honorée, reportée). Une seule fois : une
    /// convocation déjà close repart en 422, jamais en écrasement silencieux.
    /// </summary>
    [HttpPatch("{id:guid}/outcome")]
    [ProducesResponseType<CloseParentSummonsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CloseParentSummonsResult>> CloseParentSummons(
        Guid id, CloseParentSummonsOutcomeRequest request, CancellationToken cancellationToken)
        => await mediator.Send(
            new CloseParentSummonsCommand(id, request.Outcome, request.OutcomeNotes), cancellationToken);

    [HttpGet("{id:guid}/notice")]
    [ProducesResponseType<ParentNoticeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotice(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetParentNoticeQuery(id), cancellationToken));

    [HttpGet("{id:guid}/notice/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNoticePdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetParentNoticePdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("La convocation PDF générée est vide pour {ParentSummonsId}", id);
                return NotFound(new { message = "La convocation PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Convocation-{result.NoticeNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de la convocation PDF pour {ParentSummonsId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de la convocation PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
