using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetDeletedFeeCategories;

/// <summary>GET /api/v1/fee-categories/deleted — corbeille des catégories de frais de l'école courante.</summary>
public record GetDeletedFeeCategoriesQuery : IRequest<IReadOnlyList<DeletedFeeCategoryDto>>;

public record DeletedFeeCategoryDto(Guid Id, string Name, DateTimeOffset? DeletedAt);

public class GetDeletedFeeCategoriesQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedFeeCategoriesQuery, IReadOnlyList<DeletedFeeCategoryDto>>
{
    public Task<IReadOnlyList<DeletedFeeCategoryDto>> Handle(
        GetDeletedFeeCategoriesQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.FeeCategories, schoolId,
            c => new DeletedFeeCategoryDto(c.Id, c.Name, c.DeletedAt), cancellationToken);
    }
}
