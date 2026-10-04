using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Buildings.Queries.GetDeletedBuildings;

/// <summary>GET /api/v1/buildings/deleted — corbeille des bâtiments de l'école courante.</summary>
public record GetDeletedBuildingsQuery : IRequest<IReadOnlyList<DeletedBuildingDto>>;

public record DeletedBuildingDto(Guid Id, string Name, string? Description, DateTimeOffset? DeletedAt);

public class GetDeletedBuildingsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedBuildingsQuery, IReadOnlyList<DeletedBuildingDto>>
{
    public Task<IReadOnlyList<DeletedBuildingDto>> Handle(
        GetDeletedBuildingsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.Buildings, schoolId,
            b => new DeletedBuildingDto(b.Id, b.Name, b.Description, b.DeletedAt), cancellationToken);
    }
}
