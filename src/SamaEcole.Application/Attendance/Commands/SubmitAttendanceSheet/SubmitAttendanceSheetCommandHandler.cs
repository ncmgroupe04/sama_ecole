using SamaEcole.Application.Attendance;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
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
    IPublisher publisher)
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

        // Écriture de la fiche ET des lignes élève dans UNE transaction : soit l'appel entier est
        // enregistré, soit rien. Une violation de l'index unique (classe, matière, date, créneau)
        // remonte en 409 via SaveChangesAsync (AGENTS.md règle #5), jamais un doublon silencieux.
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var sheet = new AttendanceSheet
            {
                SchoolId = schoolId,
                ClassroomId = request.ClassroomId,
                SubjectId = request.SubjectId,
                SchoolYearId = activeYear.Id,
                Date = request.Date,
                Period = request.Period.Trim(),
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
                    LateMinutes = entry.Status == AttendanceStatus.Late ? entry.LateMinutes : 0
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
                    request.Period.Trim(),
                    entry.Status,
                    entry.Status == AttendanceStatus.Late ? entry.LateMinutes : 0
                ), ct);
            }

            return new SubmitAttendanceSheetResult(sheet.Id, request.Entries.Count);
        }, cancellationToken);
    }
}
