using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Deletion;
using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryItemDeletionImpact;

/// <summary>Analyse avant suppression d'un bien d'inventaire : ce qui l'empêche ou l'accompagne, sans rien modifier.</summary>
public record GetInventoryItemDeletionImpactQuery(Guid InventoryItemId) : IRequest<DeletionImpact>;

public class GetInventoryItemDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetInventoryItemDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetInventoryItemDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, InventoryItemDeletionPolicy.Policy, request.InventoryItemId, cancellationToken);
}
