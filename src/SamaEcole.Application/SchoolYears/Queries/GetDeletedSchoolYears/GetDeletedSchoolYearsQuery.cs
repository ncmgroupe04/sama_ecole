using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.SchoolYears.Queries.GetDeletedSchoolYears;

/// <summary>GET /api/v1/school-years/deleted — années scolaires archivées de l'école courante.</summary>
public record GetDeletedSchoolYearsQuery : IRequest<IReadOnlyList<DeletedSchoolYearDto>>;

public record DeletedSchoolYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, DateTimeOffset? DeletedAt);

public class GetDeletedSchoolYearsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedSchoolYearsQuery, IReadOnlyList<DeletedSchoolYearDto>>
{
    public Task<IReadOnlyList<DeletedSchoolYearDto>> Handle(
        GetDeletedSchoolYearsQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.SchoolYears, schoolId,
            y => new DeletedSchoolYearDto(y.Id, y.Label, y.StartDate, y.EndDate, y.DeletedAt), cancellationToken);
    }
}
