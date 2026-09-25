using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ClassJournal.Queries.GetClassJournal;

public class GetClassJournalQueryHandler(
    IApplicationDbContext dbContext,
    ClassJournalScopeAuthorizer scopeAuthorizer,
    ICurrentUserService currentUser,
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

        // GetOwnTeacherIdOrNullAsync renvoie null pour TOUT rôle non-Enseignant — Directeur et
        // Secrétariat (correction libre après 15 jours) MAIS AUSSI Surveillant, qui n'a lui qu'un
        // accès en lecture (ClassJournalController.CorrectRoles ne le liste pas). Sans cette
        // distinction explicite, ClassJournalEditWindow.CanCorrect traiterait ownTeacherId=null
        // comme « rôle non borné » pour le Surveillant aussi, et l'écran afficherait des boutons
        // Modifier/Supprimer qui échoueraient en 403 au clic.
        var isUnrestrictedCorrector = currentUser.Role is Role.Directeur or Role.Secretariat;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var rows = await query
            .OrderByDescending(e => e.SessionDate)
            .ThenBy(e => e.Id)
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

        // Unités pointées de la page (Évolution N°7) : une requête pour toutes les séances affichées.
        var entryIds = rows.Select(r => r.Id).ToList();
        var unitsByEntry = (await dbContext.ClassJournalEntryUnits.AsNoTracking()
                .Where(l => entryIds.Contains(l.ClassJournalEntryId))
                .Select(l => new { l.ClassJournalEntryId, l.SyllabusUnitId })
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.ClassJournalEntryId, l => l.SyllabusUnitId);

        var items = rows
            .Select(r => new ClassJournalEntryListItem(
                r.Id, r.ClassroomId, r.ClassroomName, r.SubjectId, r.SubjectName,
                r.TeacherId, r.TeacherName, r.SessionDate, r.Topic, r.Content,
                r.Homework, r.HomeworkDueDate, r.RowVersion,
                isUnrestrictedCorrector
                    || (ownTeacherId is { } teacherId
                        && ClassJournalEditWindow.CanCorrect(r.TeacherId, r.SessionDate, teacherId, today)),
                unitsByEntry[r.Id].ToList()))
            .ToList();

        return new PaginatedClassJournalEntries(items, totalCount, request.Page, request.PageSize);
    }
}
