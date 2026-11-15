using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Absences.Queries.GetAbsenceJustifications;

public class GetAbsenceJustificationsQueryHandler(IApplicationDbContext _context)
    : IRequestHandler<GetAbsenceJustificationsQuery, List<AbsenceJustificationDto>>
{
    public async Task<List<AbsenceJustificationDto>> Handle(GetAbsenceJustificationsQuery request, CancellationToken cancellationToken)
    {
        return await _context.AbsenceJustifications
            .AsNoTracking()
            .Include(a => a.Student)
            .OrderByDescending(a => a.StartDate)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new AbsenceJustificationDto
            {
                Id = a.Id,
                StudentId = a.StudentId,
                StudentFullName = a.Student.FullName,
                StudentMatricule = a.Student.Matricule,
                StartDate = a.StartDate,
                EndDate = a.EndDate,
                Days = a.EndDate.DayNumber - a.StartDate.DayNumber + 1,
                Reason = a.Reason,
                AuthorizedReturnDate = a.AuthorizedReturnDate,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
