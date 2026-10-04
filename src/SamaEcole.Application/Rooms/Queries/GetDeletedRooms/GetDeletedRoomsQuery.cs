using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Rooms.Queries.GetDeletedRooms;

/// <summary>GET /api/v1/rooms/deleted — corbeille des salles de l'école courante.</summary>
public record GetDeletedRoomsQuery : IRequest<IReadOnlyList<DeletedRoomDto>>;

public record DeletedRoomDto(Guid Id, string Name, Guid BuildingId, DateTimeOffset? DeletedAt);

public class GetDeletedRoomsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedRoomsQuery, IReadOnlyList<DeletedRoomDto>>
{
    public Task<IReadOnlyList<DeletedRoomDto>> Handle(GetDeletedRoomsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.Rooms, schoolId,
            r => new DeletedRoomDto(r.Id, r.Name, r.BuildingId, r.DeletedAt), cancellationToken);
    }
}
