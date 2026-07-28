using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Attendance.Queries.GetTeacherAttendances;

public class GetTeacherAttendancesQueryHandler(IApplicationDbContext _context)
    : IRequestHandler<GetTeacherAttendancesQuery, List<TeacherAttendanceDto>>
{
    public async Task<List<TeacherAttendanceDto>> Handle(GetTeacherAttendancesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.TeacherAttendances
            .AsNoTracking()
            .Include(t => t.Teacher)
            .AsQueryable();

        if (request.Date.HasValue)
        {
            var dateValue = request.Date.Value.Date;
            query = query.Where(t => t.Date.Date == dateValue);
        }

        return await query
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Teacher.FullName)
            .Select(t => new TeacherAttendanceDto
            {
                Id = t.Id,
                TeacherId = t.TeacherId,
                TeacherFullName = t.Teacher.FullName,
                TeacherMatricule = t.Teacher.Matricule,
                Date = t.Date,
                Status = t.Status,
                LateMinutes = t.LateMinutes,
                Reason = t.Reason,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
