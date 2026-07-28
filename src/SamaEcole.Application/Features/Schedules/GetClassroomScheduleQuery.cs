using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;


namespace SamaEcole.Application.Features.Schedules;

//[Authorize(Roles = "SuperAdmin, Directeur, Secretariat, Enseignant")]
public record GetClassroomScheduleQuery(Guid ClassroomId) : IRequest<List<ScheduleSlotDto>>;

public class GetClassroomScheduleQueryHandler(IApplicationDbContext context) 
    : IRequestHandler<GetClassroomScheduleQuery, List<ScheduleSlotDto>>
{
    public async Task<List<ScheduleSlotDto>> Handle(GetClassroomScheduleQuery request, CancellationToken cancellationToken)
    {
        return await context.ScheduleSlots.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
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
