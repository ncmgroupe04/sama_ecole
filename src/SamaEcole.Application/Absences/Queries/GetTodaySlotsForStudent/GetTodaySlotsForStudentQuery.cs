using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Absences.Queries.GetTodaySlotsForStudent;

/// <summary>
/// GET /api/v1/absences/today-slots?studentId=&amp;date= — les cours du jour de la CLASSE d'un élève, pour que la
/// Vie Scolaire choisisse celui que le billet d'entrée vise (Évolution N°5, arbitrage B12). Le cours en cours,
/// à défaut le prochain, est signalé pour être présélectionné.
///
/// « En cours » se lit sur l'heure UTC : l'école est à Dakar, UTC+0 toute l'année (même hypothèse que la
/// semaine de travail, Évolution N°3). Le jour est celui d'aujourd'hui par défaut ; un jour de REPOS de
/// l'établissement ne renvoie aucun cours — l'appel y est de toute façon refusé, le billet reste alors possible
/// sans cours visé. Lecture seule (règle #7) ; l'école vient du JWT.
/// </summary>
public record GetTodaySlotsForStudentQuery(Guid StudentId, DateOnly? Date = null)
    : IRequest<IReadOnlyList<StudentSlotDto>>;

/// <param name="IsCurrent">Le cours a lieu maintenant (uniquement pour la date du jour).</param>
/// <param name="IsNext">Premier cours à venir aujourd'hui, quand aucun n'est en cours.</param>
/// <param name="TicketStatus">Statut d'un billet actif déjà émis pour cet élève et ce cours ce jour-là, sinon null.</param>
public record StudentSlotDto(
    Guid SlotId,
    Guid SubjectId,
    string SubjectName,
    string TeacherName,
    TimeOnly Start,
    TimeOnly End,
    string Label,
    bool IsCurrent,
    bool IsNext,
    EntryTicketStatus? TicketStatus);

public class GetTodaySlotsForStudentQueryValidator : AbstractValidator<GetTodaySlotsForStudentQuery>
{
    public GetTodaySlotsForStudentQueryValidator() => RuleFor(x => x.StudentId).NotEmpty();
}

public class GetTodaySlotsForStudentQueryHandler(
    IApplicationDbContext dbContext,
    WorkingDayGuard workingDayGuard,
    TimeProvider timeProvider)
    : IRequestHandler<GetTodaySlotsForStudentQuery, IReadOnlyList<StudentSlotDto>>
{
    public async Task<IReadOnlyList<StudentSlotDto>> Handle(
        GetTodaySlotsForStudentQuery request, CancellationToken cancellationToken)
    {
        var classroomId = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => (Guid?)s.ClassroomId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Student", request.StudentId.ToString());

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var date = request.Date ?? today;

        var workingDays = await workingDayGuard.GetWorkingDaysAsync(cancellationToken);
        if (!SchoolWeek.IsWorkingDay(workingDays, date))
        {
            return [];
        }

        var dayOfWeek = date.DayOfWeek;
        var slots = await dbContext.ScheduleSlots.AsNoTracking()
            .Where(s => s.ClassroomId == classroomId && s.DayOfWeek == dayOfWeek)
            .OrderBy(s => s.StartTime)
            .Select(s => new
            {
                s.Id, s.SubjectId, SubjectName = s.Subject.Name, TeacherName = s.Teacher.FullName, s.StartTime, s.EndTime
            })
            .ToListAsync(cancellationToken);

        var slotIds = slots.Select(s => s.Id).ToList();
        var day = date.ToDateTime(TimeOnly.MinValue);
        var tickets = await dbContext.LateArrivals.AsNoTracking()
            .Where(l => l.StudentId == request.StudentId
                        && l.Date == day
                        && l.TargetScheduleSlotId != null
                        && slotIds.Contains(l.TargetScheduleSlotId.Value)
                        && (l.Status == EntryTicketStatus.Issued || l.Status == EntryTicketStatus.Accepted))
            .Select(l => new { SlotId = l.TargetScheduleSlotId!.Value, l.Status })
            .ToListAsync(cancellationToken);

        // « En cours » / « prochain » n'ont de sens que pour aujourd'hui.
        var time = TimeOnly.FromDateTime(now.UtcDateTime);
        var isToday = date == today;
        var current = isToday ? slots.FirstOrDefault(s => s.StartTime <= time && time < s.EndTime) : null;
        var next = isToday && current is null ? slots.FirstOrDefault(s => s.StartTime > time) : null;

        return slots
            .Select(s => new StudentSlotDto(
                s.Id, s.SubjectId, s.SubjectName, s.TeacherName, s.StartTime, s.EndTime,
                SlotPeriod.Label(s.StartTime, s.EndTime),
                current is not null && s.Id == current.Id,
                next is not null && s.Id == next.Id,
                tickets.FirstOrDefault(t => t.SlotId == s.Id)?.Status))
            .ToList();
    }
}
