using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Beds.GetDeletedBeds;

/// <summary>GET /api/v1/boarding/beds/deleted — corbeille des lits de l'école courante.</summary>
public record GetDeletedBedsQuery : IRequest<IReadOnlyList<DeletedBoardingItemDto>>;

public class GetDeletedBedsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedBedsQuery, IReadOnlyList<DeletedBoardingItemDto>>
{
    public Task<IReadOnlyList<DeletedBoardingItemDto>> Handle(
        GetDeletedBedsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.Beds, schoolId,
            b => new DeletedBoardingItemDto(b.Id, "Lit " + b.BedNumber.ToString(), b.DormitoryRoomId, b.DeletedAt),
            cancellationToken);
    }
}
