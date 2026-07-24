using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Discipline.Queries.GetDisciplineRecords;

public class GetDisciplineRecordsQueryHandler(IApplicationDbContext _context)
    : IRequestHandler<GetDisciplineRecordsQuery, List<DisciplineRecordDto>>
{
    public async Task<List<DisciplineRecordDto>> Handle(GetDisciplineRecordsQuery request, CancellationToken cancellationToken)
    {
        return await _context.DisciplineRecords
            .AsNoTracking()
            .Include(d => d.Student)
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .Select(d => new DisciplineRecordDto
            {
                Id = d.Id,
                StudentId = d.StudentId,
                StudentFullName = d.Student.FullName,
                StudentMatricule = d.Student.Matricule,
                Date = d.Date,
                Type = d.Type,
                Reason = d.Reason,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
