using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolCalendar.Queries.ListSchoolClosures;

/// <summary>
/// GET /api/v1/school-calendar/closures?schoolYearId= — les fermetures d'une année, dans l'ordre du
/// calendrier. Sans <c>SchoolYearId</c>, celles de l'année ACTIVE ; aucune année active = liste vide.
/// </summary>
public record ListSchoolClosuresQuery(Guid? SchoolYearId = null) : IRequest<IReadOnlyList<SchoolClosureDto>>;

public class ListSchoolClosuresQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListSchoolClosuresQuery, IReadOnlyList<SchoolClosureDto>>
{
    public async Task<IReadOnlyList<SchoolClosureDto>> Handle(
        ListSchoolClosuresQuery request, CancellationToken cancellationToken)
    {
        var yearId = request.SchoolYearId;

        if (yearId is null)
        {
            yearId = await dbContext.SchoolYears.AsNoTracking()
                .Where(y => y.IsActive)
                .Select(y => (Guid?)y.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (yearId is null)
            {
                return [];
            }
        }

        // Le Global Query Filter + la RLS bornent à l'école courante : l'année d'une autre école
        // renvoie une liste vide, jamais une fuite.
        return await dbContext.SchoolClosures.AsNoTracking()
            .Where(c => c.SchoolYearId == yearId)
            .OrderBy(c => c.StartDate).ThenBy(c => c.EndDate)
            .Select(c => new SchoolClosureDto(c.Id, c.SchoolYearId, c.StartDate, c.EndDate, c.Kind, c.Label, c.LabelAr))
            .ToListAsync(cancellationToken);
    }
}
