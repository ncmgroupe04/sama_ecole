using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>Un groupe pointable pour une session : un pavillon, une classe ou un Oustaz. <paramref name="StudentCount"/> : élèves à pointer.</summary>
public record AttendanceGroupDto(Guid Id, string Name, int StudentCount);

/// <summary>
/// GET /api/v1/boarding/attendance/groups?sessionId= — les groupes que l'écran propose pour une session, selon son public : les
/// pavillons (internes), les classes qui ont des pensionnaires (demi-pensionnaires ou tous), les Oustaz actifs (Halqa). Le module
/// Pédagogie n'est pas requis : une école Daara sans Pédagogie obtient quand même ses classes.
/// </summary>
public record ListAttendanceGroupsQuery(Guid SessionId) : IRequest<IReadOnlyList<AttendanceGroupDto>>;

public class ListAttendanceGroupsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListAttendanceGroupsQuery, IReadOnlyList<AttendanceGroupDto>>
{
    public async Task<IReadOnlyList<AttendanceGroupDto>> Handle(ListAttendanceGroupsQuery request, CancellationToken cancellationToken)
    {
        var session = await SessionRoster.GetAnySessionAsync(dbContext, request.SessionId, cancellationToken);

        return session.Audience switch
        {
            AttendanceAudience.Interns => await DormitoriesAsync(cancellationToken),
            AttendanceAudience.Halqa => await InstructorsAsync(cancellationToken),
            _ => await ClassroomsAsync(session.Audience == AttendanceAudience.HalfBoarders, cancellationToken)
        };
    }

    private async Task<IReadOnlyList<AttendanceGroupDto>> DormitoriesAsync(CancellationToken cancellationToken)
    {
        var counts = (await (from be in dbContext.BoardingEnrollments.AsNoTracking()
                             join bed in dbContext.Beds on be.BedId equals bed.Id
                             join room in dbContext.DormitoryRooms on bed.DormitoryRoomId equals room.Id
                             where be.IsActive && be.Regime == BoardingRegime.Interne
                             group be by room.DormitoryId into g
                             select new { DormitoryId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.DormitoryId, x => x.Count);

        var dormitories = await dbContext.Dormitories.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name }).ToListAsync(cancellationToken);

        return dormitories.Select(d => new AttendanceGroupDto(d.Id, d.Name, counts.GetValueOrDefault(d.Id))).ToList();
    }

    private async Task<IReadOnlyList<AttendanceGroupDto>> InstructorsAsync(CancellationToken cancellationToken)
    {
        var counts = (await dbContext.Students.AsNoTracking()
                .Where(s => s.InstructorId != null)
                .GroupBy(s => s.InstructorId!.Value)
                .Select(g => new { InstructorId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.InstructorId, x => x.Count);

        var instructors = await dbContext.Instructors.AsNoTracking()
            .Where(i => i.Status == EntityStatus.Active).OrderBy(i => i.FullName)
            .Select(i => new { i.Id, i.FullName }).ToListAsync(cancellationToken);

        return instructors.Select(i => new AttendanceGroupDto(i.Id, i.FullName, counts.GetValueOrDefault(i.Id))).ToList();
    }

    private async Task<IReadOnlyList<AttendanceGroupDto>> ClassroomsAsync(bool halfOnly, CancellationToken cancellationToken)
    {
        var counts = (await (from be in dbContext.BoardingEnrollments.AsNoTracking()
                             join s in dbContext.Students on be.StudentId equals s.Id
                             where be.IsActive && (!halfOnly || be.Regime == BoardingRegime.DemiPensionnaire)
                             group be by s.ClassroomId into g
                             select new { ClassroomId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.ClassroomId, x => x.Count);

        var ids = counts.Keys.ToList();
        var classrooms = await dbContext.Classrooms.AsNoTracking()
            .Where(c => ids.Contains(c.Id)).OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);

        return classrooms.Select(c => new AttendanceGroupDto(c.Id, c.Name, counts[c.Id])).ToList();
    }
}
