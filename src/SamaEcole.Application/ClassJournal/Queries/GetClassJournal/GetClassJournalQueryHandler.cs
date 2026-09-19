using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ClassJournal.Queries.GetClassJournal;

public class GetClassJournalQueryHandler(
    IApplicationDbContext dbContext,
    ClassJournalScopeAuthorizer scopeAuthorizer,
    TimeProvider timeProvider)
    : IRequestHandler<GetClassJournalQuery, PaginatedClassJournalEntries>
{
    public async Task<PaginatedClassJournalEntries> Handle(
        GetClassJournalQuery request, CancellationToken cancellationToken)
    {
        // Aucun filtre SchoolId manuel : le Global Query Filter + la policy RLS l'appliquent déjà
        // (AGENTS.md règle #2) — le réécrire ici donnerait l'illusion que c'est ce code qui protège.
        var query = dbContext.ClassJournalEntries.AsNoTracking();

        if (request.ClassroomId is { } classroomId)
        {
            query = query.Where(e => e.ClassroomId == classroomId);
        }

        if (request.SubjectId is { } subjectId)
        {
            query = query.Where(e => e.SubjectId == subjectId);
        }

        if (request.PeriodStart is { } periodStart)
        {
            query = query.Where(e => e.SessionDate >= periodStart);
        }

        if (request.PeriodEnd is { } periodEnd)
        {
            query = query.Where(e => e.SessionDate <= periodEnd);
        }

        // Compté AVANT la pagination : le total de la recherche, pas le nombre de lignes renvoyées.
        var totalCount = await query.CountAsync(cancellationToken);

        // Résolu UNE FOIS pour toute la page, pas par ligne : ownTeacherId ne dépend que du compte
        // courant, jamais de l'entrée regardée (ClassJournalScopeAuthorizer.CanCorrect ci-dessous).
        var ownTeacherId = await scopeAuthorizer.GetOwnTeacherIdOrNullAsync(cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var rows = await query
            .OrderByDescending(e => e.SessionDate)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new
            {
                e.Id,
                e.ClassroomId,

                // Jointures côté base. Une classe/matière/fiche enseignant supprimée (soft delete)
                // sort du Global Query Filter et rendrait la sous-requête vide : on l'affiche alors
                // explicitement plutôt que de faire disparaître l'entrée de la liste.
                ClassroomName = dbContext.Classrooms.AsNoTracking()
                    .Where(c => c.Id == e.ClassroomId).Select(c => c.Name).FirstOrDefault() ?? "Classe supprimée",

                e.SubjectId,
                SubjectName = dbContext.Subjects.AsNoTracking()
                    .Where(s => s.Id == e.SubjectId).Select(s => s.Name).FirstOrDefault() ?? "Matière supprimée",

                e.TeacherId,
                TeacherName = dbContext.Teachers.AsNoTracking()
                    .Where(t => t.Id == e.TeacherId).Select(t => t.FullName).FirstOrDefault() ?? "Enseignant supprimé",

                e.SessionDate,
                e.Topic,
                e.Content,
                e.Homework,
                e.HomeworkDueDate,
                RowVersion = EF.Property<uint>(e, "xmin")
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new ClassJournalEntryListItem(
                r.Id, r.ClassroomId, r.ClassroomName, r.SubjectId, r.SubjectName,
                r.TeacherId, r.TeacherName, r.SessionDate, r.Topic, r.Content,
                r.Homework, r.HomeworkDueDate, r.RowVersion,
                ClassJournalEditWindow.CanCorrect(r.TeacherId, r.SessionDate, ownTeacherId, today)))
            .ToList();

        return new PaginatedClassJournalEntries(items, totalCount, request.Page, request.PageSize);
    }
}
