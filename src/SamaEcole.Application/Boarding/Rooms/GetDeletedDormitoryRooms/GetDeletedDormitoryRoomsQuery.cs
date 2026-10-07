using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Rooms.GetDeletedDormitoryRooms;

/// <summary>GET /api/v1/boarding/rooms/deleted — corbeille des chambres de l'école courante.</summary>
public record GetDeletedDormitoryRoomsQuery : IRequest<IReadOnlyList<DeletedBoardingItemDto>>;

public class GetDeletedDormitoryRoomsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedDormitoryRoomsQuery, IReadOnlyList<DeletedBoardingItemDto>>
{
    public Task<IReadOnlyList<DeletedBoardingItemDto>> Handle(
        GetDeletedDormitoryRoomsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.DormitoryRooms, schoolId,
            r => new DeletedBoardingItemDto(r.Id, r.Name, r.DormitoryId, r.DeletedAt), cancellationToken);
    }
}
