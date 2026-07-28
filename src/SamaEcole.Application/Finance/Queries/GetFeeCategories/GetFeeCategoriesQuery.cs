using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFeeCategories;

/// <summary>
/// GET /api/v1/finance/fee-categories — ticket JGK-F01.
/// Les catégories de l'école courante, et elles seules (tenant du JWT, RLS + Global Query Filter).
/// </summary>
public record GetFeeCategoriesQuery : IRequest<IReadOnlyList<FeeCategoryDto>>;

public record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
