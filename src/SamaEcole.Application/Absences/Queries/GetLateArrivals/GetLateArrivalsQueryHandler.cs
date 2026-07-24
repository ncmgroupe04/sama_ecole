using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Absences.Queries.GetLateArrivals;

public class GetLateArrivalsQueryHandler(IApplicationDbContext _context)
    : IRequestHandler<GetLateArrivalsQuery, List<LateArrivalDto>>
{
    public async Task<List<LateArrivalDto>> Handle(GetLateArrivalsQuery request, CancellationToken cancellationToken)
    {
        return await _context.LateArrivals
            .AsNoTracking()
            .Include(l => l.Student)
            .OrderByDescending(l => l.Date)
            .ThenByDescending(l => l.CreatedAt)
            .Select(l => new LateArrivalDto
            {
                Id = l.Id,
                StudentId = l.StudentId,
                StudentFullName = l.Student.FullName,
                StudentMatricule = l.Student.Matricule,
                Date = l.Date,
                Minutes = l.Minutes,
                Reason = l.Reason,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
