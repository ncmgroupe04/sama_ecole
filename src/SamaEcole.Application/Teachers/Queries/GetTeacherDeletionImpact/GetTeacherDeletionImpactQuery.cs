using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Teachers.Deletion;
using MediatR;

namespace SamaEcole.Application.Teachers.Queries.GetTeacherDeletionImpact;

/// <summary>GET /teachers/{id}/deletion-impact — ce qui empêche ou accompagne la suppression d'enseignant, sans rien modifier.</summary>
public record GetTeacherDeletionImpactQuery(Guid TeacherId) : IRequest<DeletionImpact>;

public class GetTeacherDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTeacherDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetTeacherDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, TeacherDeletionPolicy.Policy, request.TeacherId, cancellationToken);
}
