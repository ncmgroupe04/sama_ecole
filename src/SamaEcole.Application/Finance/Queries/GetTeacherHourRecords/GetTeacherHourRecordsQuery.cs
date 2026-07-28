using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTeacherHourRecords;

public record TeacherHourRecordDto(Guid Id, DateOnly Date, decimal Hours, string? Note);

public record GetTeacherHourRecordsQuery(Guid EmployeeContractId, int? Month, int? Year) : IRequest<List<TeacherHourRecordDto>>;

public class GetTeacherHourRecordsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTeacherHourRecordsQuery, List<TeacherHourRecordDto>>
{
    public async Task<List<TeacherHourRecordDto>> Handle(GetTeacherHourRecordsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.TeacherHourRecords.AsNoTracking()
            .Where(r => r.EmployeeContractId == request.EmployeeContractId);

        if (request.Month.HasValue)
        {
            query = query.Where(r => r.Date.Month == request.Month.Value);
        }

        if (request.Year.HasValue)
        {
            query = query.Where(r => r.Date.Year == request.Year.Value);
        }

        return await query
            .OrderBy(r => r.Date)
            .Select(r => new TeacherHourRecordDto(r.Id, r.Date, r.Hours, r.Note))
            .ToListAsync(cancellationToken);
    }
}
