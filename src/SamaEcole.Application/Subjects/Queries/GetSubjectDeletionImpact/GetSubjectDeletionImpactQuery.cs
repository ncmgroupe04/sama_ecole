using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subjects.Deletion;
using MediatR;

namespace SamaEcole.Application.Subjects.Queries.GetSubjectDeletionImpact;

/// <summary>GET /subjects/{id}/deletion-impact — ce qui empêche ou accompagne la suppression d'matière, sans rien modifier.</summary>
public record GetSubjectDeletionImpactQuery(Guid SubjectId) : IRequest<DeletionImpact>;

public class GetSubjectDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSubjectDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetSubjectDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, SubjectDeletionPolicy.Policy, request.SubjectId, cancellationToken);
}
