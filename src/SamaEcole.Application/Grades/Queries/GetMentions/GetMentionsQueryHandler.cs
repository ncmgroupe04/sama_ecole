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

        // Seuils par défaut sur le barème de référence des mentions (/20), jamais sur
        // SchoolSettings.GradingScale — voir MentionScales.
        return MentionDefaults.ForScale(MentionScales.Reference)
            .Select(m => new MentionDto(null, m.Label, m.MinAverage))
            .ToList();
    }
}
