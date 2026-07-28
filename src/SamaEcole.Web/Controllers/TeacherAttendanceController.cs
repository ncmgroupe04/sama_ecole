using SamaEcole.Application.Attendance.Commands.CreateTeacherAttendance;
using SamaEcole.Application.Attendance.Queries.GetTeacherAttendances;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/teacher-attendance")]
[Authorize(Roles = $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Surveillant)}")]
public class TeacherAttendanceController(IMediator _mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TeacherAttendanceDto>>> GetTeacherAttendances([FromQuery] DateTime? date)
    {
        return await _mediator.Send(new GetTeacherAttendancesQuery(date));
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> CreateTeacherAttendance(CreateTeacherAttendanceCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(id);
    }
}
