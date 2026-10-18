using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Rooms.Deletion;
using MediatR;

namespace SamaEcole.Application.Rooms.Queries.GetRoomDeletionImpact;

/// <summary>Analyse avant suppression d'une salle : ce qui l'empêche ou l'accompagne, sans rien modifier.</summary>
public record GetRoomDeletionImpactQuery(Guid RoomId) : IRequest<DeletionImpact>;

public class GetRoomDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetRoomDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetRoomDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, RoomDeletionPolicy.Policy, request.RoomId, cancellationToken);
}
