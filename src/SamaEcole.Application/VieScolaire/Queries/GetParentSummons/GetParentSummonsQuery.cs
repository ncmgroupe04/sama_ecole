using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.VieScolaire.Queries.GetParentSummons;

public record ParentSummonsDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentFullName { get; init; } = null!;
    public string StudentMatricule { get; init; } = null!;
    public DateTimeOffset ScheduledAt { get; init; }
    public string Reason { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
}

public record GetParentSummonsQuery : IRequest<List<ParentSummonsDto>>;

public class GetParentSummonsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetParentSummonsQuery, List<ParentSummonsDto>>
{
    public async Task<List<ParentSummonsDto>> Handle(GetParentSummonsQuery request, CancellationToken cancellationToken)
    {
        return await (
            from p in dbContext.ParentSummons.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on p.StudentId equals s.Id
            orderby p.ScheduledAt descending
            select new ParentSummonsDto
            {
                Id = p.Id,
                StudentId = p.StudentId,
                StudentFullName = s.FullName,
                StudentMatricule = s.Matricule,
                ScheduledAt = p.ScheduledAt,
                Reason = p.Reason,
                CreatedAt = p.CreatedAt
            }).ToListAsync(cancellationToken);
    }
}
