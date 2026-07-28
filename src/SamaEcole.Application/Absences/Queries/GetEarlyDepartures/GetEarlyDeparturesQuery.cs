using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Absences.Queries.GetEarlyDepartures;

public record EarlyDepartureDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentFullName { get; init; } = null!;
    public string StudentMatricule { get; init; } = null!;
    public DateTime Date { get; init; }
    public TimeOnly DepartureTime { get; init; }
    public string Reason { get; init; } = null!;
    public string? PickedUpBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record GetEarlyDeparturesQuery : IRequest<List<EarlyDepartureDto>>;

public class GetEarlyDeparturesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEarlyDeparturesQuery, List<EarlyDepartureDto>>
{
    public async Task<List<EarlyDepartureDto>> Handle(GetEarlyDeparturesQuery request, CancellationToken cancellationToken)
    {
        return await (
            from d in dbContext.EarlyDepartures.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on d.StudentId equals s.Id
            orderby d.CreatedAt descending
            select new EarlyDepartureDto
            {
                Id = d.Id,
                StudentId = d.StudentId,
                StudentFullName = s.FullName,
                StudentMatricule = s.Matricule,
                Date = d.Date,
                DepartureTime = d.DepartureTime,
                Reason = d.Reason,
                PickedUpBy = d.PickedUpBy,
                CreatedAt = d.CreatedAt
            }).ToListAsync(cancellationToken);
    }
}
