using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Grades.Queries.GetDeletedMentions;

/// <summary>GET /api/v1/mentions/deleted — corbeille des mentions de l'école courante.</summary>
public record GetDeletedMentionsQuery : IRequest<IReadOnlyList<DeletedMentionDto>>;

public record DeletedMentionDto(Guid Id, string Label, decimal MinAverage, DateTimeOffset? DeletedAt);

public class GetDeletedMentionsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedMentionsQuery, IReadOnlyList<DeletedMentionDto>>
{
    public Task<IReadOnlyList<DeletedMentionDto>> Handle(
        GetDeletedMentionsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.Mentions, schoolId,
            m => new DeletedMentionDto(m.Id, m.Label, m.MinAverage, m.DeletedAt), cancellationToken);
    }
}
