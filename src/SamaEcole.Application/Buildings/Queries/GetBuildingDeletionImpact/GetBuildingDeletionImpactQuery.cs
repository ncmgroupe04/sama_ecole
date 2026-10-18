using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Buildings.Deletion;
using MediatR;

namespace SamaEcole.Application.Buildings.Queries.GetBuildingDeletionImpact;

/// <summary>Analyse avant suppression d'un bâtiment : ce qui l'empêche ou l'accompagne, sans rien modifier.</summary>
public record GetBuildingDeletionImpactQuery(Guid BuildingId) : IRequest<DeletionImpact>;

public class GetBuildingDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetBuildingDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetBuildingDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, BuildingDeletionPolicy.Policy, request.BuildingId, cancellationToken);
}
