using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Attendance.Queries.GetAttendanceSlots;

/// <summary>
/// GET /api/v1/attendance/slots?classroomId=&amp;date= — les cours d'emploi du temps d'une classe pour une
/// date (Évolution N°5), de quoi choisir le créneau de l'appel.
///
/// Un jour de REPOS de l'établissement (SchoolSettings.WorkingDays) ne renvoie aucun cours : l'appel y est
/// de toute façon refusé. Un ENSEIGNANT ne reçoit que SES cours (il ne fait l'appel que de ceux-là) ; la Vie
/// Scolaire, le Secrétariat et le Directeur les reçoivent tous — c'est ce qui permet de remplacer un absent.
///
/// Lecture seule (règle #7). L'école vient du JWT : aucun filtre SchoolId à la main.
/// </summary>
public record GetAttendanceSlotsQuery(Guid ClassroomId, DateOnly Date) : IRequest<IReadOnlyList<AttendanceSlotDto>>;

/// <param name="Label">« 08:00-10:00 » — le libellé qu'aura le créneau de la fiche (SlotPeriod.Label).</param>
/// <param name="SheetId">La fiche d'appel déjà saisie pour ce cours à cette date, sinon null.</param>
public record AttendanceSlotDto(
    Guid SlotId,
    Guid SubjectId,
    string SubjectName,
    Guid TeacherId,
    string TeacherName,
    TimeOnly Start,
    TimeOnly End,
    string Label,
    Guid? SheetId);

public class GetAttendanceSlotsQueryValidator : AbstractValidator<GetAttendanceSlotsQuery>
{
    public GetAttendanceSlotsQueryValidator()
    {
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.Date).NotEmpty();
    }
}

public class GetAttendanceSlotsQueryHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    WorkingDayGuard workingDayGuard)
    : IRequestHandler<GetAttendanceSlotsQuery, IReadOnlyList<AttendanceSlotDto>>
{
    public async Task<IReadOnlyList<AttendanceSlotDto>> Handle(
        GetAttendanceSlotsQuery request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);
        }

        // Jour de repos : aucun cours à proposer (l'appel y est refusé par WorkingDayGuard).
        var workingDays = await workingDayGuard.GetWorkingDaysAsync(cancellationToken);
        if (!SchoolWeek.IsWorkingDay(workingDays, request.Date))
        {
            return [];
        }

        var dayOfWeek = request.Date.DayOfWeek;

        var query = dbContext.ScheduleSlots.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId && s.DayOfWeek == dayOfWeek);

        // Un Enseignant ne voit que ses créneaux — sa fiche est retrouvée depuis son COMPTE (jamais depuis
        // un identifiant fourni par le client, règle #10).
        if (currentUser.Role == Role.Enseignant)
        {
            var userId = currentUser.UserId;
            var teacherId = await dbContext.Teachers.AsNoTracking()
                .Where(t => t.UserId == userId)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (teacherId is null)
            {
                return [];
            }

            query = query.Where(s => s.TeacherId == teacherId);
        }

        var slots = await query
            .OrderBy(s => s.StartTime)
            .Select(s => new
            {
                s.Id, s.SubjectId, SubjectName = s.Subject.Name, s.TeacherId, TeacherName = s.Teacher.FullName,
                s.StartTime, s.EndTime
            })
            .ToListAsync(cancellationToken);

        var slotIds = slots.Select(s => s.Id).ToList();
        var sheets = await dbContext.AttendanceSheets.AsNoTracking()
            .Where(a => a.Date == request.Date && a.ScheduleSlotId != null && slotIds.Contains(a.ScheduleSlotId.Value))
            .Select(a => new { SlotId = a.ScheduleSlotId!.Value, a.Id })
            .ToListAsync(cancellationToken);

        return slots
            .Select(s => new AttendanceSlotDto(
                s.Id, s.SubjectId, s.SubjectName, s.TeacherId, s.TeacherName,
                s.StartTime, s.EndTime, SlotPeriod.Label(s.StartTime, s.EndTime),
                sheets.FirstOrDefault(x => x.SlotId == s.Id)?.Id))
            .ToList();
    }
}
