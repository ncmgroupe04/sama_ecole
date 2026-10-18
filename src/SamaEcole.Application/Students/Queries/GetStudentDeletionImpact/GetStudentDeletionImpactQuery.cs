using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students.Deletion;
using MediatR;

namespace SamaEcole.Application.Students.Queries.GetStudentDeletionImpact;

/// <summary>GET /students/{id}/deletion-impact — ce qui empêche ou accompagne la suppression, sans rien modifier.</summary>
public record GetStudentDeletionImpactQuery(Guid StudentId) : IRequest<DeletionImpact>;

public class GetStudentDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetStudentDeletionImpactQuery request, CancellationToken cancellationToken) =>
        new StudentDeletionAnalyzer(dbContext).AnalyzeAsync(request.StudentId, cancellationToken);
}
