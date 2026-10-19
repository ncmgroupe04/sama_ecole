using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Deletion;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFeeCategoryDeletionImpact;

/// <summary>Analyse avant suppression d'une catégorie de frais : ce qui l'empêche ou l'accompagne, sans rien modifier.</summary>
public record GetFeeCategoryDeletionImpactQuery(Guid FeeCategoryId) : IRequest<DeletionImpact>;

public class GetFeeCategoryDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFeeCategoryDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetFeeCategoryDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, FeeCategoryDeletionPolicy.Policy, request.FeeCategoryId, cancellationToken);
}
