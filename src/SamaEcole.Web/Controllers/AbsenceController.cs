using SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;
using SamaEcole.Application.Absences.Commands.CreateEarlyDeparture;
using SamaEcole.Application.Absences.Commands.CreateLateArrival;
using SamaEcole.Application.Absences.Queries.GetAbsenceJustifications;
using SamaEcole.Application.Absences.Queries.GetEarlyDepartures;
using SamaEcole.Application.Absences.Queries.GetLateArrivals;
using SamaEcole.Application.Absences.Queries.GetTodaySlotsForStudent;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/absences")]
[Authorize(Roles = $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Surveillant)}")]
[RequireModule(SchoolModule.Pedagogy)]
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

    /// <summary>
    /// Cours du jour de la classe d'un élève (Évolution N°5) : de quoi choisir le cours que le billet d'entrée
    /// vise. Le cours en cours, à défaut le prochain, est signalé. Un jour de repos ne renvoie aucun cours.
    /// </summary>
    [HttpGet("today-slots")]
    public async Task<ActionResult<IReadOnlyList<StudentSlotDto>>> GetTodaySlots(
        [FromQuery] Guid studentId, [FromQuery] DateOnly? date)
    {
        return Ok(await _mediator.Send(new GetTodaySlotsForStudentQuery(studentId, date)));
    }

    [HttpPost("late-arrivals")]
    public async Task<ActionResult<Guid>> CreateLateArrival(CreateLateArrivalCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(id);
    }

    [HttpGet("early-departures")]
    public async Task<ActionResult<List<EarlyDepartureDto>>> GetEarlyDepartures()
    {
        return await _mediator.Send(new GetEarlyDeparturesQuery());
    }

    [HttpPost("early-departures")]
    public async Task<ActionResult<Guid>> CreateEarlyDeparture(CreateEarlyDepartureCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(id);
    }
}
