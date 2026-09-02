using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
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

    /// <summary>Suite donnée à l'entretien — voir CloseParentSummonsCommand.</summary>
    public ParentSummonsStatus Status { get; init; }
    public string? OutcomeNotes { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
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
            // Les convocations SANS suite d'abord, la plus ancienne en tête : ce sont les seules sur
            // lesquelles il reste quelque chose à faire. Un tri purement chronologique enterrait une
            // convocation oubliée d'octobre sous les entretiens déjà clos de juin.
            orderby p.Status == ParentSummonsStatus.Scheduled descending, p.ScheduledAt descending
            select new ParentSummonsDto
            {
                Id = p.Id,
                StudentId = p.StudentId,
                StudentFullName = s.FullName,
                StudentMatricule = s.Matricule,
                ScheduledAt = p.ScheduledAt,
                Reason = p.Reason,
                CreatedAt = p.CreatedAt,
                Status = p.Status,
                OutcomeNotes = p.OutcomeNotes,
                ClosedAt = p.ClosedAt
            }).ToListAsync(cancellationToken);
    }
}
