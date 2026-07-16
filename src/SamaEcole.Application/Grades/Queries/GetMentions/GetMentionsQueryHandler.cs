using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Queries.GetMentions;

public class GetMentionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetMentionsQuery, IReadOnlyList<MentionDto>>
{
    public async Task<IReadOnlyList<MentionDto>> Handle(GetMentionsQuery request, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Mentions
            .AsNoTracking()
            .OrderByDescending(m => m.MinAverage)
            .ToListAsync(cancellationToken);

        if (stored.Count > 0)
        {
            return stored.Select(m => new MentionDto(m.Id, m.Label, m.MinAverage)).ToList();
        }

        var gradingScale = await GradingScaleGuard.ResolveScaleAsync(dbContext, cancellationToken);
        return MentionDefaults.ForScale(gradingScale)
            .Select(m => new MentionDto(null, m.Label, m.MinAverage))
            .ToList();
    }
}
