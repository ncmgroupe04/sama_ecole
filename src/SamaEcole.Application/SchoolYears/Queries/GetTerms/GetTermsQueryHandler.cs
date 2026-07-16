using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolYears.Queries.GetTerms;

public class GetTermsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTermsQuery, IReadOnlyList<TermDto>>
{
    public async Task<IReadOnlyList<TermDto>> Handle(GetTermsQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la requête à l'école courante : un
        // SchoolYearId d'une autre école renvoie simplement une liste vide, jamais une fuite.
        return await dbContext.Terms
            .AsNoTracking()
            .Where(t => t.SchoolYearId == request.SchoolYearId)
            .OrderBy(t => t.Order)
            .Select(t => new TermDto(t.Id, t.Label, t.Order, t.StartDate, t.EndDate))
            .ToListAsync(cancellationToken);
    }
}
