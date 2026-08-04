using SamaEcole.Application.Discipline.Commands.CreateDisciplineRecord;
using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;
using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPvPdf;
using SamaEcole.Application.Discipline.Queries.GetDisciplineRecords;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/discipline")]
[Authorize(Roles = $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Surveillant)}")]
public class DisciplineController(IMediator _mediator, ILogger<DisciplineController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DisciplineRecordDto>>> GetDisciplineRecords()
    {
        return await _mediator.Send(new GetDisciplineRecordsQuery());
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> CreateDisciplineRecord(CreateDisciplineRecordCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(id);
    }

    [HttpGet("{id:guid}/pv")]
    [ProducesResponseType<DisciplinaryPvDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pv(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetDisciplinaryPvQuery(id), cancellationToken));

    [HttpGet("{id:guid}/pv/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PvPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _mediator.Send(new GetDisciplinaryPvPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le PV de discipline PDF généré est vide pour la sanction {DisciplineRecordId}", id);
                return NotFound(new { message = "Le PV de discipline PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"PV-Discipline-{result.PvNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du PV de discipline PDF pour {DisciplineRecordId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération du PV de discipline PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
