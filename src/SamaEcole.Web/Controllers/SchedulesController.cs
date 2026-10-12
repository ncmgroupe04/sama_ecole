using SamaEcole.Application.Features.Schedules;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/schedules")]
[Authorize(Roles = "Directeur,Secretariat,Enseignant,SuperAdmin")]
public class SchedulesController(ISender mediator) : ControllerBase
{
    [HttpGet("teacher/{teacherId:guid}")]
    [ProducesResponseType<IReadOnlyList<ScheduleSlotDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeacherSchedule(Guid teacherId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetTeacherScheduleQuery(teacherId), cancellationToken));
    }

    [HttpGet("classroom/{classroomId:guid}")]
    [ProducesResponseType<IReadOnlyList<ScheduleSlotDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClassroomSchedule(Guid classroomId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetClassroomScheduleQuery(classroomId), cancellationToken));
    }

    /// <summary>Alternance français / arabe d'une classe (module Franco-Arabe) — constat en lecture seule.</summary>
    [HttpGet("classroom/{classroomId:guid}/sections")]
    [ProducesResponseType<SectionScheduleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClassroomSections(Guid classroomId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetClassroomSectionScheduleQuery(classroomId), cancellationToken));
    }

    /// <summary>Alternance français / arabe d'un enseignant ; repère l'enseignant qui couvre les deux sections.</summary>
    [HttpGet("teacher/{teacherId:guid}/sections")]
    [ProducesResponseType<SectionScheduleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeacherSections(Guid teacherId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetTeacherSectionScheduleQuery(teacherId), cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSlot(
        [FromBody] CreateScheduleSlotCommand command,
        CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetTeacherSchedule), new { teacherId = command.TeacherId }, id);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateSlot(
        Guid id,
        [FromBody] UpdateScheduleSlotCommand command,
        CancellationToken cancellationToken)
    {
        if (id != command.Id)
        {
            return BadRequest();
        }

        await mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSlot(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteScheduleSlotCommand(id), cancellationToken);
        return NoContent();
    }
}
