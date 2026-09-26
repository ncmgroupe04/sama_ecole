using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;

public class InitializeAttendanceSheetQueryHandler(
    IApplicationDbContext dbContext,
    AttendanceScopeAuthorizer scopeAuthorizer,
    WorkingDayGuard workingDayGuard)
    : IRequestHandler<InitializeAttendanceSheetQuery, AttendanceRosterDto>
{
    public async Task<AttendanceRosterDto> Handle(InitializeAttendanceSheetQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent tout à l'école courante : une classe/matière d'une
        // autre école est simplement introuvable, jamais exposée.
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == request.ClassroomId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);

        var subject = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == request.SubjectId)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);

        // L'appel se rattache à l'année ACTIVE (comme la soumission) — c'est aussi elle qui sert à
        // vérifier la portée de l'enseignant. Sans année active, l'appel n'a rien à quoi se rattacher.
        var activeYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure("SchoolYear", "Aucune année scolaire active. Activez une année scolaire avant de faire l'appel.")
            ]);

        // Portée : un Enseignant ne peut ouvrir la grille que pour ses classes/matières assignées
        // (403 sinon). Directeur/Secrétariat non bornés.
        await scopeAuthorizer.EnsureCanTakeAttendanceAsync(request.ClassroomId, request.SubjectId, activeYear.Id, cancellationToken);

        // Créneau d'emploi du temps (Évolution N°5) : mêmes contrôles que la soumission.
        var resolved = await scopeAuthorizer.ResolveSlotAsync(
            request.ScheduleSlotId, request.ClassroomId, request.SubjectId, request.Date, request.Period, cancellationToken);

        // Jour de repos de l'établissement (Évolution N°3) : pas de feuille d'appel à ouvrir ce jour-là.
        await workingDayGuard.EnsureWorkingDayAsync(request.Date, nameof(request.Date), cancellationToken);

        // Un élève en abandon ou transféré sur l'année ACTIVE sort des futures listes de présence :
        // sa scolarité dans cette classe s'est arrêtée, même si sa fiche pointe encore la classe
        // (ChangeEnrollmentStatusCommand ne déplace jamais Student.ClassroomId). Les notes et
        // paiements déjà enregistrés restent, eux, intacts — cette exclusion ne concerne que l'appel.
        var excludedStudentIds = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.SchoolYearId == activeYear.Id
                        && e.ClassroomId == request.ClassroomId
                        && (e.Status == EnrollmentStatus.DroppedOut || e.Status == EnrollmentStatus.Transferred))
            .Select(e => e.StudentId)
            .ToListAsync(cancellationToken);

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId && !excludedStudentIds.Contains(s.Id))
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName })
            .ToListAsync(cancellationToken);

        // Fiche déjà saisie pour cette clé ? On récupère les statuts pour pré-remplir la grille.
        var existingSheetId = await dbContext.AttendanceSheets.AsNoTracking()
            .Where(a => a.ClassroomId == request.ClassroomId
                        && a.SubjectId == request.SubjectId
                        && a.Date == request.Date
                        && a.Period == resolved.Period)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Requête exécutée même sans fiche existante : le prédicat « != null » la rend simplement
        // vide dans ce cas, ce qui évite d'avoir à nommer le type anonyme pour un dictionnaire vide.
        var existingStatuses = await dbContext.StudentAttendances.AsNoTracking()
            .Where(sa => existingSheetId != null && sa.AttendanceSheetId == existingSheetId)
            .ToDictionaryAsync(sa => sa.StudentId, sa => new { sa.Status, sa.LateMinutes }, cancellationToken);

        // Billets d'entrée actifs qui touchent CE cours à CETTE date (Évolution N°5, Complément N°5 bis) : ils
        // présélectionnent l'état de l'élève tant que l'appel n'est pas fait, et la feuille les signale (« en
        // attente d'acceptation »). Le billet peut VISER ce cours (retard : « Retard », ou rien à changer si l'élève
        // arrive pile à l'heure) ou l'avoir MANQUÉ avant l'arrivée (« Absent (justifié) »).
        var ticketsByStudent = new Dictionary<Guid, (Guid Id, int Minutes, EntryTicketStatus? Status, bool Missed)>();
        if (resolved.SlotId is { } slotId)
        {
            var day = request.Date.ToDateTime(TimeOnly.MinValue);
            var tickets = await dbContext.LateArrivals.AsNoTracking()
                .Where(l => l.Date == day
                            && (l.Status == EntryTicketStatus.Issued || l.Status == EntryTicketStatus.Accepted)
                            && (l.TargetScheduleSlotId == slotId
                                || (l.MissedScheduleSlotIds != null && l.MissedScheduleSlotIds.Contains(slotId))))
                .Select(l => new { l.Id, l.StudentId, l.Minutes, l.Status, Missed = l.TargetScheduleSlotId != slotId })
                .ToListAsync(cancellationToken);

            // Un cours visé l'emporte sur un cours manqué si, exceptionnellement, deux billets le touchent.
            ticketsByStudent = tickets
                .GroupBy(t => t.StudentId)
                .ToDictionary(g => g.Key, g =>
                {
                    var t = g.OrderBy(x => x.Missed).First();
                    return (t.Id, t.Minutes, t.Status, t.Missed);
                });
        }

        var rows = students
            .Select(s =>
            {
                existingStatuses.TryGetValue(s.Id, out var recorded);
                var hasTicket = ticketsByStudent.TryGetValue(s.Id, out var ticket);

                // Un appel déjà saisi l'emporte toujours ; sinon un billet présélectionne « Retard » (cours visé, minutes
                // > 0) ou « Absent (justifié) » (cours manqué). Cours visé à 0 minute : le billet est seulement rattaché.
                string? preselected = null;
                if (hasTicket)
                {
                    preselected = ticket.Missed
                        ? nameof(AttendanceStatus.JustifiedAbsence)
                        : ticket.Minutes > 0 ? nameof(AttendanceStatus.Late) : null;
                }

                var status = recorded?.Status.ToString() ?? preselected;
                var lateMinutes = recorded?.LateMinutes ?? (hasTicket && !ticket.Missed ? ticket.Minutes : 0);

                return new AttendanceRosterRow(
                    s.Id,
                    s.Matricule,
                    s.FullName,
                    status,
                    lateMinutes,
                    hasTicket ? ticket.Id : null,
                    hasTicket ? EntryTicketNumber.For(ticket.Id) : null,
                    hasTicket ? ticket.Status?.ToString() : null);
            })
            .ToList();

        return new AttendanceRosterDto(
            classroom.Id,
            classroom.Name,
            subject.Id,
            subject.Name,
            request.Date,
            resolved.Period,
            existingSheetId is not null,
            rows);
    }
}
