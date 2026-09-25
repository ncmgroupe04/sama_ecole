using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.ReportCards.Queries.GetCouncilRules;

/// <summary>GET /api/v1/report-cards/council-rules — seuils du conseil de classe de l'école (défauts MEN sinon).</summary>
public record GetCouncilRulesQuery : IRequest<CouncilRules>;

public class GetCouncilRulesQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetCouncilRulesQuery, CouncilRules>
{
    public Task<CouncilRules> Handle(GetCouncilRulesQuery request, CancellationToken cancellationToken)
        => CouncilRules.ResolveAsync(dbContext, cancellationToken);
}
