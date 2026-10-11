using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Attendance.RecordAttendance;

/// <summary>
/// POST /api/v1/boarding/attendance — pointage de nuit COLLECTIF d'un pavillon : une ligne par pensionnaire, enregistrée en
/// upsert sur <c>(séjour, date)</c>. Le résultat est donné LIGNE PAR LIGNE : une ligne refusée (hors pavillon, doublon,
/// pensionnaire en sortie déclaré présent) n'empêche pas les autres. Date : pas dans le futur, dans l'année active. Un
/// pensionnaire en sortie ce soir-là n'est accepté qu'absent ; sans note, la ligne indique « En permission ».
/// </summary>
public record RecordAttendanceCommand(DateOnly Date, Guid DormitoryId, IReadOnlyList<AttendanceEntryInput> Entries)
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

        var stayIds = await AttendanceRules.DormitoryStays(dbContext, request.DormitoryId)
            .AsNoTracking().Select(b => b.Id).ToListAsync(cancellationToken);
        var inDormitory = stayIds.ToHashSet();
        var onLeave = await AttendanceRules.OnLeaveAsync(dbContext, stayIds, request.Date, cancellationToken);

        var existing = (await dbContext.BoardingAttendances
                .Where(a => a.Date == request.Date && stayIds.Contains(a.BoardingEnrollmentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.BoardingEnrollmentId);

        var seen = new HashSet<Guid>();
        var lines = new List<AttendanceLineResult>();

        foreach (var entry in request.Entries)
        {
            if (!seen.Add(entry.BoarderId))
            {
                lines.Add(Rejected(entry, "DUPLICATE_ENTRY", "Ce pensionnaire figure plusieurs fois : la première ligne est retenue."));
                continue;
            }

            if (!inDormitory.Contains(entry.BoarderId))
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

            if (existing.TryGetValue(entry.BoarderId, out var row))
            {
                row.IsPresent = entry.IsPresent;
                row.Note = note;
            }
            else
            {
                dbContext.BoardingAttendances.Add(new BoardingAttendance
                {
                    SchoolId = dormitory.SchoolId,
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
            when (ex.TechnicalDetail.Contains("UX_boarding_attendances_night", StringComparison.OrdinalIgnoreCase))
        {
            // Course : un autre opérateur a saisi la même nuit entre notre lecture et notre écriture.
            throw new BusinessRuleException(
                "Ce pointage vient d'être saisi par quelqu'un d'autre. Rechargez la feuille d'appel.", "ATTENDANCE_CONFLICT");
        }

        var saved = lines.Count(l => l.Outcome == "Saved");
        return new RecordAttendanceResult(request.Date, request.DormitoryId, saved, lines.Count - saved, lines);
    }

    private static AttendanceLineResult Rejected(AttendanceEntryInput entry, string code, string message) =>
        new(entry.BoarderId, "Rejected", code, message);
}
