using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Buildings.Queries.GetDeletedBuildings;

/// <summary>GET /api/v1/buildings/deleted — corbeille des bâtiments de l'école courante.</summary>
public record GetDeletedBuildingsQuery : IRequest<IReadOnlyList<DeletedBuildingDto>>;

public record DeletedBuildingDto(Guid Id, string Name, string? Description, DateTimeOffset? DeletedAt);

public class GetDeletedBuildingsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedBuildingsQuery, IReadOnlyList<DeletedBuildingDto>>
{
    public async Task<IReadOnlyList<DeletedBuildingDto>> Handle(
        GetDeletedBuildingsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Filtres EF levés pour lire les tombstones : SchoolId explicite, RLS en seconde barrière.
        return await dbContext.Buildings.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.SchoolId == schoolId && b.IsDeleted)
            .OrderByDescending(b => b.DeletedAt)
            .Select(b => new DeletedBuildingDto(b.Id, b.Name, b.Description, b.DeletedAt))
            .ToListAsync(cancellationToken);
    }
}
