using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFeeCategories;

public class GetFeeCategoriesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFeeCategoriesQuery, IReadOnlyList<FeeCategoryDto>>
{
    public async Task<IReadOnlyList<FeeCategoryDto>> Handle(
        GetFeeCategoriesQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.FeeCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new FeeCategoryDto(c.Id, c.Name, c.IsRecurring))
            .ToListAsync(cancellationToken);
    }
}
