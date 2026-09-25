using SamaEcole.Application.Attendance;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;

public class SubmitAttendanceSheetCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    AttendanceScopeAuthorizer scopeAuthorizer,
    WorkingDayGuard workingDayGuard,
    IPublisher publisher,
    IKpiCacheService kpiCache)
    : IRequestHandler<SubmitAttendanceSheetCommand, SubmitAttendanceSheetResult>
{
    public async Task<SubmitAttendanceSheetResult> Handle(SubmitAttendanceSheetCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var takenByUserId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        var classroomExists = await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken);
        if (!classroomExists)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);
        }

        var subjectExists = await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken);
        if (!subjectExists)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);
        }

        // L'appel se rattache à l'année ACTIVE (résolue serveur, jamais fournie par le client).
        var activeYear = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure("SchoolYear", "Aucune année scolaire active. Activez une année scolaire avant de faire l'appel.")
            ]);

        // Portée : un Enseignant ne peut faire l'appel que pour ses classes/matières assignées (403).
        await scopeAuthorizer.EnsureCanTakeAttendanceAsync(request.ClassroomId, request.SubjectId, activeYear.Id, cancellationToken);

        // Créneau d'emploi du temps (Évolution N°5) : vérifié, et le libellé du créneau en est DÉRIVÉ. Sans
        // créneau, le mode libre — la période saisie par le client — est strictement celui d'avant.
        var resolved = await scopeAuthorizer.ResolveSlotAsync(
            request.ScheduleSlotId, request.ClassroomId, request.SubjectId, request.Date, request.Period, cancellationToken);

        // Jour de repos de l'établissement (Évolution N°3) : aucun appel ne s'y saisit.
        await workingDayGuard.EnsureWorkingDayAsync(request.Date, nameof(request.Date), cancellationToken);

        // Billets d'entrée actifs visant CE cours à CETTE date (Évolution N°5) : chaque ligne d'un élève qui en
        // a un lui est rattachée. Le statut, lui, reste CELUI QUE L'ENSEIGNANT A SAISI — la feuille présélectionne
        // « Retard », mais un élève marqué absent le reste, et son billet demeure en attente d'acceptation.
        var ticketByStudent = new Dictionary<Guid, Guid>();
        if (resolved.SlotId is { } ticketSlotId)
        {
            var day = request.Date.ToDateTime(TimeOnly.MinValue);
            // Complément N°5 bis : un billet touche ce cours soit qu'il le VISE, soit qu'il l'ait fait MANQUER.
            ticketByStudent = (await dbContext.LateArrivals.AsNoTracking()
                    .Where(l => l.Date == day
                                && (l.Status == EntryTicketStatus.Issued || l.Status == EntryTicketStatus.Accepted)
                                && (l.TargetScheduleSlotId == ticketSlotId
                                    || (l.MissedScheduleSlotIds != null && l.MissedScheduleSlotIds.Contains(ticketSlotId))))
                    .Select(l => new { l.StudentId, l.Id, Missed = l.TargetScheduleSlotId != ticketSlotId })
                    .ToListAsync(cancellationToken))
                .GroupBy(t => t.StudentId)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Missed).First().Id);
        }

        // Tous les élèves de l'appel doivent appartenir à CETTE classe : un statut posé sur un élève
        // d'une autre classe (ou d'une autre école, déjà masqué par la RLS) est une erreur de saisie.
        var classStudentIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var submittedIds = request.Entries.Select(e => e.StudentId).ToList();
        var foreignIds = submittedIds.Except(classStudentIds).ToList();
        if (foreignIds.Count > 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Entries), "Un ou plusieurs élèves n'appartiennent pas à cette classe.")
            ]);
        }

        // Complément N°5 bis (C8/C9) : un retard n'a plus d'autre source qu'un BILLET D'ENTRÉE. L'enseignant pointe
        // présence ou absence ; une ligne « Retard » n'est acceptée que si un billet actif la porte (feuille
        // présélectionnée par le billet, renvoyée telle quelle). En mode libre aucun billet ne peut être rattaché :
        // tout « Retard » y est donc refusé. Les retards déjà en base (historique) ne passent pas par ici.
        if (request.Entries.Any(e => e.Status == AttendanceStatus.Late && !ticketByStudent.ContainsKey(e.StudentId)))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Entries),
                    "Le retard s'enregistre par un billet d'entrée (Surveillance › Billets d'entrée) : marquez l'élève présent ou absent.")
            ]);
        }

        // Écriture de la fiche ET des lignes élève dans UNE transaction : soit l'appel entier est
        // enregistré, soit rien. Une violation de l'index unique (classe, matière, date, créneau)
        // remonte en 409 via SaveChangesAsync (AGENTS.md règle #5), jamais un doublon silencieux.
        var result = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var sheet = new AttendanceSheet
            {
                SchoolId = schoolId,
                ClassroomId = request.ClassroomId,
                SubjectId = request.SubjectId,
                SchoolYearId = activeYear.Id,
                Date = request.Date,
                Period = resolved.Period,
                ScheduleSlotId = resolved.SlotId,
                TakenByUserId = takenByUserId
            };

            dbContext.AttendanceSheets.Add(sheet);

            foreach (var entry in request.Entries)
            {
                dbContext.StudentAttendances.Add(new StudentAttendance
                {
                    SchoolId = schoolId,
                    AttendanceSheetId = sheet.Id,
                    StudentId = entry.StudentId,
                    Status = entry.Status,
                    // Invariant : les minutes de retard n'ont de sens que pour Late (validé en amont,
                    // reforcé ici pour que la donnée en base ne puisse pas être incohérente).
                    LateMinutes = entry.Status == AttendanceStatus.Late ? entry.LateMinutes : 0,
                    EntryTicketId = ticketByStudent.TryGetValue(entry.StudentId, out var ticketId) ? ticketId : null
                });
            }

            await dbContext.SaveChangesAsync(ct);

            // Notification pour les retards et absences
            foreach (var entry in request.Entries.Where(e => e.Status is AttendanceStatus.Late or AttendanceStatus.UnjustifiedAbsence or AttendanceStatus.JustifiedAbsence))
            {
                await publisher.Publish(new SamaEcole.Application.Attendance.Events.AttendanceRecordedEvent(
                    schoolId,
                    entry.StudentId,
                    request.ClassroomId,
                    request.SubjectId,
                    request.Date,
                    resolved.Period,
                    entry.Status,
                    entry.Status == AttendanceStatus.Late ? entry.LateMinutes : 0
                ), ct);
            }

            return new SubmitAttendanceSheetResult(sheet.Id, request.Entries.Count);
        }, cancellationToken);

        // Inconditionnel, pas seulement sur la branche Retard/Absence ci-dessus : un appel « tout
        // présent » change aussi le taux de présence du dashboard Directeur.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return result;
    }
}
