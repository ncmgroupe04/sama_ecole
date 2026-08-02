using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.PublicDirectory.Queries.GetPublicSchools;

public class GetPublicSchoolsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPublicSchoolsQuery, PaginatedPublicSchools>
{
    public async Task<PaginatedPublicSchools> Handle(
        GetPublicSchoolsQuery request, CancellationToken cancellationToken)
    {
        // La source est la VUE, jamais dbContext.Schools : le filtre de consentement et la liste des
        // colonnes exposées sont tenus par la BASE (migration AddPublicSchoolDirectory). Rien de ce qui
        // est écrit ici ne peut donc élargir le périmètre — au pire le restreindre.
        var query = dbContext.PublicSchoolDirectory.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // ToLower() plutôt que EF.Functions.ILike : SamaEcole.Application ne doit connaître aucun
            // provider (Clean Architecture) — Npgsql traduit ToLower() en lower(). Même choix que
            // GetStudentsQueryHandler.
            var search = request.Search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim().ToLower();
            query = query.Where(s => s.City != null && s.City.ToLower() == city);
        }

        if (!string.IsNullOrWhiteSpace(request.Region))
        {
            var region = request.Region.Trim().ToLower();
            query = query.Where(s => s.Region != null && s.Region.ToLower() == region);
        }

        if (!string.IsNullOrWhiteSpace(request.Cycle))
        {
            // Comparaison sur la valeur BRUTE de l'énumération, telle que stockée dans classrooms puis
            // agrégée par la vue. Le validateur a déjà garanti que c'est un membre réel de CycleType ;
            // on normalise la casse pour accepter « primaire » comme « Primaire » dans l'URL.
            var cycle = request.Cycle.Trim();
            query = query.Where(s => s.Cycles.Contains(cycle));
        }

        // Compté AVANT la pagination : c'est le total de la recherche, pas le nombre de lignes de la
        // page — sans quoi la pagination afficherait toujours une seule page.
        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(s => s.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // Les libellés de cycle sont mis en forme CÔTÉ CLR : l'ordre pédagogique et les accents
        // (« Collège ») ne se traduisent pas en SQL, et la vue doit rester une projection brute.
        var items = rows.Select(Map).ToList();

        return new PaginatedPublicSchools(items, totalCount, request.Page, request.PageSize);
    }

    internal static PublicSchoolDto Map(Domain.Entities.PublicSchoolListing row) =>
        new(row.Id,
            row.Name,
            row.City,
            row.Region,
            row.PublicDescription,
            row.LogoUrl,
            row.Address,
            row.Phone,
            row.Email,
            CycleLabels.ToLabels(row.Cycles));
}
