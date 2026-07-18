using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Queries.GetStudents;

public class GetStudentsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentsQuery, PaginatedStudents>
{
    public async Task<PaginatedStudents> Handle(GetStudentsQuery request, CancellationToken cancellationToken)
    {
        // Aucun filtre sur SchoolId ici, volontairement : le Global Query Filter l'applique
        // automatiquement, et la policy RLS PostgreSQL le rejouerait même si ce filtre disparaissait
        // un jour (AGENTS.md règle #2). Le réécrire à la main donnerait l'illusion que c'est LUI qui
        // protège, et le rendrait facile à oublier sur la prochaine requête.
        var query = dbContext.Students.AsNoTracking();

        if (request.ClassroomId is { } classroomId)
        {
            query = query.Where(s => s.ClassroomId == classroomId);
        }

        if (!string.IsNullOrWhiteSpace(request.Gender))
        {
            query = query.Where(s => s.Gender == request.Gender);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Recherche insensible à la casse : « diop » doit trouver « Diop ».
            //
            // Volontairement écrit avec ToLower() et non avec EF.Functions.ILike, qui serait plus
            // direct : ILike est une extension Npgsql, et SamaEcole.Application ne doit connaître
            // aucun provider (Clean Architecture — la couche métier ignore PostgreSQL). Npgsql
            // traduit ToLower() en lower(), le comportement est donc le même.
            var search = request.Search.Trim().ToLower();

            query = query.Where(s =>
                s.FullName.ToLower().Contains(search) ||
                s.Matricule.ToLower().Contains(search));
        }

        // Compté AVANT la pagination : c'est le total de la recherche, pas le nombre de lignes
        // renvoyées — sans quoi la pagination afficherait toujours une seule page.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => s.FullName)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new StudentListItem(
                s.Id,
                s.Matricule,
                s.FullName,
                s.BirthDate,
                s.Gender,
                s.ClassroomId,

                // Jointure côté base. Une classe supprimée (soft delete) sort du Global Query Filter
                // et rendrait la sous-requête vide : on l'affiche alors explicitement plutôt que de
                // faire disparaître l'élève de la liste.
                dbContext.Classrooms
                    .AsNoTracking()
                    .Where(c => c.Id == s.ClassroomId)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? "Classe supprimée",

                s.GuardianName,
                s.GuardianPhone))
            .ToListAsync(cancellationToken);

        return new PaginatedStudents(items, totalCount, request.Page, request.PageSize);
    }
}
