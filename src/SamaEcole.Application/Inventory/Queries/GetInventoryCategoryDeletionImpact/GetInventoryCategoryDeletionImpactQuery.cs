using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Deletion;
using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryCategoryDeletionImpact;

/// <summary>Analyse avant suppression d'une catégorie d'inventaire : ce qui l'empêche ou l'accompagne, sans rien modifier.</summary>
public record GetInventoryCategoryDeletionImpactQuery(Guid InventoryCategoryId) : IRequest<DeletionImpact>;

public class GetInventoryCategoryDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetInventoryCategoryDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetInventoryCategoryDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, InventoryCategoryDeletionPolicy.Policy, request.InventoryCategoryId, cancellationToken);
}
