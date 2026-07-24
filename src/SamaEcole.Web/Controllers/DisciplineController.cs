using SamaEcole.Application.Discipline.Commands.CreateDisciplineRecord;
using SamaEcole.Application.Discipline.Queries.GetDisciplineRecords;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/discipline")]
[Authorize(Roles = $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Surveillant)}")]
public class DisciplineController(IMediator _mediator) : ControllerBase
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
}
