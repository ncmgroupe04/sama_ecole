using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;


namespace SamaEcole.Application.Features.Schedules;

//[Authorize(Roles = "SuperAdmin, Directeur, Secretariat, Enseignant")]
public record GetTeacherScheduleQuery(Guid TeacherId) : IRequest<List<ScheduleSlotDto>>;

public class ScheduleSlotDto
{
    public Guid Id { get; init; }
    public Guid TeacherId { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public Guid ClassroomId { get; init; }
    public string ClassroomName { get; init; } = string.Empty;
    public Guid SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public DayOfWeek DayOfWeek { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public string? RoomNumber { get; init; }
    public bool IsTeacherSubmitted { get; init; }
}

public class GetTeacherScheduleQueryHandler(IApplicationDbContext context) 
    : IRequestHandler<GetTeacherScheduleQuery, List<ScheduleSlotDto>>
{
    public async Task<List<ScheduleSlotDto>> Handle(GetTeacherScheduleQuery request, CancellationToken cancellationToken)
    {
        return await context.ScheduleSlots.AsNoTracking()
            .Where(s => s.TeacherId == request.TeacherId)
            .Select(s => new ScheduleSlotDto
            {
                Id = s.Id,
                TeacherId = s.TeacherId,
                TeacherName = s.Teacher.FullName,
                ClassroomId = s.ClassroomId,
                ClassroomName = s.Classroom.Name,
                SubjectId = s.SubjectId,
                SubjectName = s.Subject.Name,
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                RoomNumber = s.RoomNumber,
                IsTeacherSubmitted = s.IsTeacherSubmitted
            })
            .ToListAsync(cancellationToken);
    }
}
