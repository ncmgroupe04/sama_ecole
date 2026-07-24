using SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;
using SamaEcole.Application.Absences.Commands.CreateLateArrival;
using SamaEcole.Application.Absences.Queries.GetAbsenceJustifications;
using SamaEcole.Application.Absences.Queries.GetLateArrivals;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/absences")]
[Authorize(Roles = $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Surveillant)}")]
public class AbsenceController(IMediator _mediator) : ControllerBase
{
    [HttpGet("justifications")]
    public async Task<ActionResult<List<AbsenceJustificationDto>>> GetAbsenceJustifications()
    {
        return await _mediator.Send(new GetAbsenceJustificationsQuery());
    }

    [HttpPost("justifications")]
    public async Task<ActionResult<Guid>> CreateAbsenceJustification(CreateAbsenceJustificationCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(id);
    }

    [HttpGet("late-arrivals")]
    public async Task<ActionResult<List<LateArrivalDto>>> GetLateArrivals()
    {
        return await _mediator.Send(new GetLateArrivalsQuery());
    }

    [HttpPost("late-arrivals")]
    public async Task<ActionResult<Guid>> CreateLateArrival(CreateLateArrivalCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(id);
    }
}
