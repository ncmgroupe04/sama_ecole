using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Queries.GetTeachers;

public class GetTeachersQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTeachersQuery, PaginatedTeachers>
{
    public async Task<PaginatedTeachers> Handle(GetTeachersQuery request, CancellationToken cancellationToken)
    {
        // Aucun filtre sur SchoolId ici, volontairement : le Global Query Filter l'applique déjà,
        // et la policy RLS PostgreSQL le rejouerait même en son absence (AGENTS.md règle #2).
        var query = dbContext.Teachers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Insensible à la casse, comme GetStudentsQueryHandler — Npgsql traduit ToLower() en lower().
            var search = request.Search.Trim().ToLower();

            query = query.Where(t =>
                t.FullName.ToLower().Contains(search) ||
                t.Matricule.ToLower().Contains(search) ||
                t.Email.ToLower().Contains(search));
        }

        // Compté AVANT la pagination : le total de la recherche, pas le nombre de lignes renvoyées.
        var totalCount = await query.CountAsync(cancellationToken);

        var page = await query
            .OrderBy(t => t.FullName)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new { t.Id, t.Matricule, t.FullName, t.Email, t.Phone, t.PhotoUrl, t.PhotoData, t.Status })
            .ToListAsync(cancellationToken);

        // Une seconde requête plutôt qu'une jointure imbriquée par ligne (comme le ferait un
        // Select(... => dbContext.TeacherSubjects.Where(...))) : le nombre d'enseignants d'une page
        // est borné par PageSize, donc pas de N+1 significatif, mais la lisibilité y gagne.
        var teacherIds = page.Select(t => t.Id).ToList();

        var subjectsByTeacher = await dbContext.TeacherSubjects
            .AsNoTracking()
            .Where(ts => teacherIds.Contains(ts.TeacherId))
            .Select(ts => new { ts.TeacherId, SubjectName = dbContext.Subjects
                .AsNoTracking()
                .Where(s => s.Id == ts.SubjectId)
                .Select(s => s.Name)
                .FirstOrDefault() ?? "Matière supprimée" })
            .ToListAsync(cancellationToken);

        var items = page
            .Select(t => new TeacherListItem(
                t.Id,
                t.Matricule,
                t.FullName,
                t.Email,
                t.Phone,
                t.PhotoUrl,
                PhotoDisplay.ToDisplayUrl(t.PhotoData, t.PhotoUrl),
                t.Status.ToString(),
                subjectsByTeacher
                    .Where(s => s.TeacherId == t.Id)
                    .Select(s => s.SubjectName)
                    .ToList()))
            .ToList();

        return new PaginatedTeachers(items, totalCount, request.Page, request.PageSize);
    }
}
