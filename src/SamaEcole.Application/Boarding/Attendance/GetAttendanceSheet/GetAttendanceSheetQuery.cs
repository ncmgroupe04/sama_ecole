using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Attendance.GetAttendanceSheet;

/// <summary>
/// GET /api/v1/boarding/attendance?dormitoryId=&amp;date= — feuille d'appel du soir : les internes du pavillon, triés par
/// chambre puis lit. Une ligne déjà pointée porte ses valeurs ; un pensionnaire en sortie est pré-rempli « absent — En
/// permission » ; les autres sont à saisir (<c>IsPresent = null</c>). Mêmes règles de date que le pointage.
/// </summary>
public record GetAttendanceSheetQuery(Guid DormitoryId, DateOnly Date) : IRequest<AttendanceSheetDto>;

public class GetAttendanceSheetQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetAttendanceSheetQuery, AttendanceSheetDto>
{
    public async Task<AttendanceSheetDto> Handle(GetAttendanceSheetQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        await AttendanceRules.EnsurePointableAsync(dbContext, request.Date, today, cancellationToken);
        var dormitory = await AttendanceRules.GetDormitoryAsync(dbContext, request.DormitoryId, cancellationToken);

        var boarders = await (from be in AttendanceRules.DormitoryStays(dbContext, request.DormitoryId).AsNoTracking()
                              join s in dbContext.Students on be.StudentId equals s.Id
                              join bed in dbContext.Beds on be.BedId equals bed.Id
                              join room in dbContext.DormitoryRooms on bed.DormitoryRoomId equals room.Id
                              orderby room.Name, bed.BedNumber
                              select new { be.Id, s.FullName, s.Matricule, RoomName = room.Name, bed.BedNumber })
            .ToListAsync(cancellationToken);

        var stayIds = boarders.Select(b => b.Id).ToList();
        var onLeave = await AttendanceRules.OnLeaveAsync(dbContext, stayIds, request.Date, cancellationToken);
        var recorded = (await dbContext.BoardingAttendances.AsNoTracking()
                .Where(a => a.Date == request.Date && stayIds.Contains(a.BoardingEnrollmentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.BoardingEnrollmentId);

        var lines = boarders.Select(b =>
        {
            var away = onLeave.Contains(b.Id);

            if (recorded.TryGetValue(b.Id, out var row))
            {
                return new AttendanceSheetLineDto(b.Id, b.FullName, b.Matricule, b.RoomName, b.BedNumber, away, true, row.IsPresent, row.Note);
            }

            return away
                ? new AttendanceSheetLineDto(b.Id, b.FullName, b.Matricule, b.RoomName, b.BedNumber, true, false, false, AttendanceRules.OnLeaveNote)
                : new AttendanceSheetLineDto(b.Id, b.FullName, b.Matricule, b.RoomName, b.BedNumber, false, false, null, null);
        }).ToList();

        return new AttendanceSheetDto(request.Date, request.DormitoryId, dormitory.Name, lines);
    }
}
