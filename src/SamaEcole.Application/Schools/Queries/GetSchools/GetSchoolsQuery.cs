using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Schools.Queries.GetSchools;

/// <summary>GET /schools — ticket JGK-B01, réservé au Super Admin (openapi.yaml).</summary>
public record GetSchoolsQuery : IRequest<IReadOnlyList<SchoolSummary>>;

public record SchoolSummary(
    Guid Id,
    string Name,
    string? Address,
    string? Phone,
    EntityStatus Status,
    DateTimeOffset CreatedAt);

public class GetSchoolsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSchoolsQuery, IReadOnlyList<SchoolSummary>>
{
    public async Task<IReadOnlyList<SchoolSummary>> Handle(
        GetSchoolsQuery request,
        CancellationToken cancellationToken)
    {
        // `schools` n'est pas une table tenant : aucun filtre SchoolId ne s'y applique, le Super Admin
        // voit donc bien TOUTES les écoles. C'est le seul endroit du système où c'est le cas — et il
        // n'y voit que les métadonnées d'établissement, jamais leurs données pédagogiques ou
        // financières (docs/Volume_7_Security.md §8).
        return await dbContext.Schools
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SchoolSummary(s.Id, s.Name, s.Address, s.Phone, s.Status, s.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
