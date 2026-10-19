using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Classrooms.Deletion;
using MediatR;

namespace SamaEcole.Application.Classrooms.Queries.GetClassroomDeletionImpact;

/// <summary>GET /classrooms/{id}/deletion-impact — ce qui empêche ou accompagne la suppression d'classe, sans rien modifier.</summary>
public record GetClassroomDeletionImpactQuery(Guid ClassroomId) : IRequest<DeletionImpact>;

public class GetClassroomDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassroomDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetClassroomDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, ClassroomDeletionPolicy.Policy, request.ClassroomId, cancellationToken);
}
