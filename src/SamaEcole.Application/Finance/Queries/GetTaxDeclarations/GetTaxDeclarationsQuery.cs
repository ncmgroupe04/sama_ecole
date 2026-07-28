using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTaxDeclarations;

public record GetTaxDeclarationsQuery(int? Year = null) : IRequest<List<TaxDeclarationListItemDto>>;

public record TaxDeclarationListItemDto(
    Guid Id,
    int Month,
    int Year,
    decimal TotalIpres,
    decimal TotalCss,
    decimal TotalVrs,
    decimal TotalBrs,
    decimal TvaCollected,
    decimal TvaDeductible,
    decimal NetTva,
    decimal TotalDueToState,
    DateTimeOffset CreatedAt);

public class GetTaxDeclarationsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTaxDeclarationsQuery, List<TaxDeclarationListItemDto>>
{
    public async Task<List<TaxDeclarationListItemDto>> Handle(GetTaxDeclarationsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.TaxeDeclarations.AsNoTracking().AsQueryable();

        if (request.Year.HasValue)
        {
            query = query.Where(t => t.Year == request.Year.Value);
        }

        return await query
            .OrderByDescending(t => t.Year).ThenByDescending(t => t.Month)
            .Select(t => new TaxDeclarationListItemDto(
                t.Id,
                t.Month,
                t.Year,
                t.TotalIpres,
                t.TotalCss,
                t.TotalVrs,
                t.TotalBrs,
                t.TvaCollected,
                t.TvaDeductible,
                t.NetTva,
                t.TotalDueToState,
                t.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
