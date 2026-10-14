using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance.RecordAttendance;

/// <summary>
/// POST /api/v1/boarding/attendance — pointage COLLECTIF d'un pavillon pour une session (par défaut « Appel de nuit ») : une ligne
/// par pensionnaire, enregistrée en upsert sur <c>(élève, session, date)</c>. Le résultat est donné LIGNE PAR LIGNE : une ligne
/// refusée (hors pavillon, doublon, pensionnaire en sortie déclaré présent) n'empêche pas les autres. Date : pas dans le futur, dans
/// l'année active. Un pensionnaire en sortie ce soir-là n'est accepté qu'absent ; sans note, la ligne indique « En permission ».
/// </summary>
public record RecordAttendanceCommand(
    DateOnly Date, Guid DormitoryId, IReadOnlyList<AttendanceEntryInput> Entries, Guid? SessionId = null)
    : IRequest<RecordAttendanceResult>, IAuditableRequest;

public class RecordAttendanceCommandValidator : AbstractValidator<RecordAttendanceCommand>
{
    private const int MaxEntries = 200;

    public RecordAttendanceCommandValidator()
    {
        RuleFor(x => x.DormitoryId).NotEmpty();
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("Indiquez la date de la nuit pointée.");
        RuleFor(x => x.Entries).NotEmpty().WithMessage("La feuille d'appel est vide.")
            .Must(e => e.Count <= MaxEntries).WithMessage($"{MaxEntries} lignes au plus par pointage.");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.BoarderId).NotEmpty();
            entry.RuleFor(e => e.Note).MaximumLength(300);
        });
    }
}

public class RecordAttendanceCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<RecordAttendanceCommand, RecordAttendanceResult>
{
    public async Task<RecordAttendanceResult> Handle(RecordAttendanceCommand request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        await AttendanceRules.EnsurePointableAsync(dbContext, request.Date, today, cancellationToken);
        var dormitory = await AttendanceRules.GetDormitoryAsync(dbContext, request.DormitoryId, cancellationToken);

        var session = await AttendanceRules.FindInternSessionAsync(dbContext, request.SessionId, tracking: true, cancellationToken);
        if (session is null)
        {
            // Routes d'origine, école sans aucune session : « Appel de nuit » est créée à la volée, comme la migration le fait pour
            // les écoles qui pointaient déjà.
            session = new AttendanceSession
            {
                SchoolId = dormitory.SchoolId,
                Name = AttendanceRules.DefaultNightSessionName,
                Audience = AttendanceAudience.Interns
            };
            dbContext.AttendanceSessions.Add(session);
        }

        AttendanceRules.EnsureActive(session);

        var stays = await AttendanceRules.DormitoryStays(dbContext, request.DormitoryId)
            .AsNoTracking().Select(b => new { b.Id, b.StudentId }).ToListAsync(cancellationToken);
        var studentByStay = stays.ToDictionary(b => b.Id, b => b.StudentId);
        var stayIds = studentByStay.Keys.ToList();
        var onLeave = await AttendanceRules.OnLeaveAsync(dbContext, stayIds, request.Date, cancellationToken);

        var studentIds = studentByStay.Values.ToList();
        var existing = (await dbContext.BoardingAttendances
                .Where(a => a.Date == request.Date && a.SessionId == session.Id && studentIds.Contains(a.StudentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.StudentId);

        var seen = new HashSet<Guid>();
        var lines = new List<AttendanceLineResult>();

        foreach (var entry in request.Entries)
        {
            if (!seen.Add(entry.BoarderId))
            {
                lines.Add(Rejected(entry, "DUPLICATE_ENTRY", "Ce pensionnaire figure plusieurs fois : la première ligne est retenue."));
                continue;
            }

            if (!studentByStay.TryGetValue(entry.BoarderId, out var studentId))
            {
                lines.Add(Rejected(entry, "NOT_IN_DORMITORY", "Ce pensionnaire n'a pas de lit dans ce pavillon."));
                continue;
            }

            var away = onLeave.Contains(entry.BoarderId);
            if (away && entry.IsPresent)
            {
                lines.Add(Rejected(entry, "BOARDER_ON_LEAVE", "Ce pensionnaire est en sortie cette nuit-là : il ne peut être pointé qu'absent."));
                continue;
            }

            var note = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim();
            if (!entry.IsPresent && away && note is null)
            {
                note = AttendanceRules.OnLeaveNote;
            }

            if (existing.TryGetValue(studentId, out var row))
            {
                row.IsPresent = entry.IsPresent;
                row.Note = note;
            }
            else
            {
                dbContext.BoardingAttendances.Add(new BoardingAttendance
                {
                    SchoolId = dormitory.SchoolId,
                    SessionId = session.Id,
                    StudentId = studentId,
                    BoardingEnrollmentId = entry.BoarderId,
                    Date = request.Date,
                    IsPresent = entry.IsPresent,
                    Note = note
                });
            }

            lines.Add(new AttendanceLineResult(entry.BoarderId, "Saved", null, null));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex)
            when (ex.TechnicalDetail.Contains("UX_boarding_attendances_session_day", StringComparison.OrdinalIgnoreCase)
                  || ex.TechnicalDetail.Contains("UX_attendance_sessions_name", StringComparison.OrdinalIgnoreCase))
        {
            // Course : un autre opérateur a saisi la même session le même jour (ou créé « Appel de nuit » avant nous) entre notre
            // lecture et notre écriture.
            throw new BusinessRuleException(
                "Ce pointage vient d'être saisi par quelqu'un d'autre. Rechargez la feuille d'appel.", "ATTENDANCE_CONFLICT");
        }

        var saved = lines.Count(l => l.Outcome == "Saved");
        return new RecordAttendanceResult(request.Date, request.DormitoryId, saved, lines.Count - saved, lines, session.Id);
    }

    private static AttendanceLineResult Rejected(AttendanceEntryInput entry, string code, string message) =>
        new(entry.BoarderId, "Rejected", code, message);
}
