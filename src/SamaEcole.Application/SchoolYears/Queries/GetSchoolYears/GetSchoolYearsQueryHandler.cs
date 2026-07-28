using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolYears.Queries.GetSchoolYears;

public class GetSchoolYearsQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetSchoolYearsQuery, IReadOnlyList<SchoolYearDto>>
{
    public async Task<IReadOnlyList<SchoolYearDto>> Handle(
        GetSchoolYearsQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // La plus récente d'abord : c'est celle sur laquelle on travaille, et l'écran s'ouvre dessus.
        // AsNoTracking : lecture pure, aucun suivi de modifications à payer.
        return await dbContext.SchoolYears
            .AsNoTracking()
            .OrderByDescending(y => y.StartDate)
            .Select(y => new SchoolYearDto(
                y.Id,
                y.Label,
                y.StartDate,
                y.EndDate,
                y.IsActive,
                y.EndDate < today))
            .ToListAsync(cancellationToken);
    }
}
