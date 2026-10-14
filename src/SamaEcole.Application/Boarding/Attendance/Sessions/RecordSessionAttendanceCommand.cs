using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>
/// POST /api/v1/boarding/attendance/sheet (et POST /internat/attendance pour l'Oustaz) — pointage COLLECTIF d'une session pour un
/// groupe (classe ou Halqa), en upsert sur <c>(élève, session, date)</c>. Réponse LIGNE PAR LIGNE : une ligne refusée (élève hors du
/// groupe, doublon, pensionnaire en sortie déclaré présent) n'empêche pas les autres. Un Oustaz ne pointe que sa Halqa ; une session
/// désactivée ne reçoit plus de pointage. Un jour hors des jours de la session reste pointable.
/// </summary>
public record RecordSessionAttendanceCommand(Guid SessionId, Guid? GroupId, DateOnly Date, IReadOnlyList<SessionEntryInput> Entries)
    : IRequest<RecordSessionAttendanceResult>, IAuditableRequest;

public class RecordSessionAttendanceCommandValidator : AbstractValidator<RecordSessionAttendanceCommand>
{
    private const int MaxEntries = 200;

    public RecordSessionAttendanceCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("Indiquez la date pointée.");
        RuleFor(x => x.Entries).NotEmpty().WithMessage("La feuille d'appel est vide.")
            .Must(e => e.Count <= MaxEntries).WithMessage($"{MaxEntries} lignes au plus par pointage.");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.StudentId).NotEmpty();
            entry.RuleFor(e => e.Note).MaximumLength(300);
        });
    }
}

public class RecordSessionAttendanceCommandHandler(
    IApplicationDbContext dbContext, HalqaScopeAuthorizer scope, TimeProvider timeProvider)
    : IRequestHandler<RecordSessionAttendanceCommand, RecordSessionAttendanceResult>
{
    public async Task<RecordSessionAttendanceResult> Handle(RecordSessionAttendanceCommand request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var session = await SessionRoster.GetGroupSessionAsync(dbContext, request.SessionId, tracking: false, cancellationToken);
        var groupId = await SessionRoster.ResolveGroupAsync(scope, session, request.GroupId, cancellationToken);
        AttendanceRules.EnsureActive(session);
        await AttendanceRules.EnsurePointableAsync(dbContext, request.Date, today, cancellationToken);

        var roster = await SessionRoster.LoadAsync(dbContext, session, groupId, cancellationToken);
        var memberByStudent = roster.Members.ToDictionary(m => m.StudentId);

        var stayIds = roster.Members.Where(m => m.StayId is not null).Select(m => m.StayId!.Value).ToList();
        var onLeave = await AttendanceRules.OnLeaveAsync(dbContext, stayIds, request.Date, cancellationToken);

        var studentIds = memberByStudent.Keys.ToList();
        var existing = (await dbContext.BoardingAttendances
                .Where(a => a.SessionId == session.Id && a.Date == request.Date && studentIds.Contains(a.StudentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.StudentId);

        var seen = new HashSet<Guid>();
        var lines = new List<SessionLineResult>();

        foreach (var entry in request.Entries)
        {
            if (!seen.Add(entry.StudentId))
            {
                lines.Add(Rejected(entry, "DUPLICATE_ENTRY", "Cet élève figure plusieurs fois : la première ligne est retenue."));
                continue;
            }

            if (!memberByStudent.TryGetValue(entry.StudentId, out var member))
            {
                lines.Add(Rejected(entry, "NOT_IN_GROUP", "Cet élève ne fait pas (ou plus) partie de ce groupe : rechargez la feuille d'appel."));
                continue;
            }

            var away = member.StayId is { } stay && onLeave.Contains(stay);
            if (away && entry.IsPresent)
            {
                lines.Add(Rejected(entry, "BOARDER_ON_LEAVE", "Cet élève est en sortie ce jour-là : il ne peut être pointé qu'absent."));
                continue;
            }

            var note = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim();
            if (!entry.IsPresent && away && note is null)
            {
                note = AttendanceRules.OnLeaveNote;
            }

            if (existing.TryGetValue(entry.StudentId, out var row))
            {
                row.IsPresent = entry.IsPresent;
                row.Note = note;
            }
            else
            {
                dbContext.BoardingAttendances.Add(new BoardingAttendance
                {
                    SchoolId = session.SchoolId,
                    SessionId = session.Id,
                    StudentId = entry.StudentId,
                    BoardingEnrollmentId = member.StayId,
                    Date = request.Date,
                    IsPresent = entry.IsPresent,
                    Note = note
                });
            }

            lines.Add(new SessionLineResult(entry.StudentId, "Saved", null, null));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex)
            when (ex.TechnicalDetail.Contains("UX_boarding_attendances_session_day", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "Ce pointage vient d'être saisi par quelqu'un d'autre. Rechargez la feuille d'appel.", "ATTENDANCE_CONFLICT");
        }

        var saved = lines.Count(l => l.Outcome == "Saved");
        return new RecordSessionAttendanceResult(session.Id, groupId, request.Date, saved, lines.Count - saved, lines);
    }

    private static SessionLineResult Rejected(SessionEntryInput entry, string code, string message) =>
        new(entry.StudentId, "Rejected", code, message);
}
