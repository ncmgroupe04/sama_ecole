using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Classrooms.Queries.GetDeletedClassrooms;

/// <summary>GET /api/v1/classrooms/deleted — corbeille des classes de l'école courante.</summary>
public record GetDeletedClassroomsQuery : IRequest<IReadOnlyList<DeletedClassroomDto>>;

public record DeletedClassroomDto(Guid Id, string Name, string Level, DateTimeOffset? DeletedAt);

public class GetDeletedClassroomsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedClassroomsQuery, IReadOnlyList<DeletedClassroomDto>>
{
    public Task<IReadOnlyList<DeletedClassroomDto>> Handle(
        GetDeletedClassroomsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.Classrooms, schoolId,
            c => new DeletedClassroomDto(c.Id, c.Name, c.Level, c.DeletedAt), cancellationToken);
    }
}
