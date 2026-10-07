using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Dormitories.GetDeletedDormitories;

/// <summary>GET /api/v1/boarding/dormitories/deleted — corbeille des pavillons de l'école courante.</summary>
public record GetDeletedDormitoriesQuery : IRequest<IReadOnlyList<DeletedBoardingItemDto>>;

public class GetDeletedDormitoriesQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedDormitoriesQuery, IReadOnlyList<DeletedBoardingItemDto>>
{
    public Task<IReadOnlyList<DeletedBoardingItemDto>> Handle(
        GetDeletedDormitoriesQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.Dormitories, schoolId,
            d => new DeletedBoardingItemDto(d.Id, d.Name, null, d.DeletedAt), cancellationToken);
    }
}
