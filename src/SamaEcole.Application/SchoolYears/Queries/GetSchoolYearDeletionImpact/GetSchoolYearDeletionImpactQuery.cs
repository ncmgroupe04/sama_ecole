using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.SchoolYears.Deletion;
using MediatR;

namespace SamaEcole.Application.SchoolYears.Queries.GetSchoolYearDeletionImpact;

/// <summary>
/// Analyse avant suppression d'une année scolaire : ce qui l'empêche ou l'accompagne, sans rien modifier. En mode test,
/// l'effacement définitif historique s'applique (il emporte tout, paiements compris) : l'analyse ne liste alors rien.
/// </summary>
public record GetSchoolYearDeletionImpactQuery(Guid SchoolYearId) : IRequest<DeletionImpact>;

public class GetSchoolYearDeletionImpactQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetSchoolYearDeletionImpactQuery, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(GetSchoolYearDeletionImpactQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return await SchoolYearDeletionPolicy.AnalyzeAsync(dbContext, schoolId, request.SchoolYearId, cancellationToken);
    }
}
