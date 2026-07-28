using SamaEcole.Application.Features.Disbursements;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/finance/disbursements")]
[Authorize(Roles = $"{nameof(Role.Directeur)},Finance,{nameof(Role.SuperAdmin)}")]
public class DisbursementsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DisbursementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDisbursements(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] DisbursementCategory? category,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetDisbursementsQuery(startDate, endDate, category), cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDisbursement(
        [FromBody] CreateDisbursementCommand command,
        CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetDisbursements), new { id }, id);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteDisbursement(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteDisbursementCommand(id), cancellationToken);
        return NoContent();
    }
}
