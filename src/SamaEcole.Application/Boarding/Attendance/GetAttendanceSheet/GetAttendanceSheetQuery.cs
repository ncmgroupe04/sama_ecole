using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Attendance.GetAttendanceSheet;

/// <summary>
/// GET /api/v1/boarding/attendance?dormitoryId=&amp;date=[&amp;sessionId=] — feuille d'appel d'un pavillon : les internes, triés par
/// chambre puis lit. Une ligne déjà pointée porte ses valeurs ; un pensionnaire en sortie est pré-rempli « absent — En
/// permission » ; les autres sont à saisir (<c>IsPresent = null</c>). Mêmes règles de date que le pointage. Sans
/// <paramref name="SessionId"/> : la session « Appel de nuit » (comportement d'origine).
/// </summary>
public record GetAttendanceSheetQuery(Guid DormitoryId, DateOnly Date, Guid? SessionId = null) : IRequest<AttendanceSheetDto>;

public class GetAttendanceSheetQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetAttendanceSheetQuery, AttendanceSheetDto>
{
    public Task<AttendanceSheetDto> Handle(GetAttendanceSheetQuery request, CancellationToken cancellationToken) =>
        AttendanceSheetReader.GetAsync(
            dbContext, request.DormitoryId, request.Date, request.SessionId,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
}

/// <summary>
/// Construction UNIQUE de la feuille d'appel d'un pavillon : l'écran de pointage et le registre imprimable (lot E) lisent la
/// même définition de « qui est pointable et qui est en sortie ».
/// </summary>
internal static class AttendanceSheetReader
{
    public static async Task<AttendanceSheetDto> GetAsync(
        IApplicationDbContext dbContext, Guid dormitoryId, DateOnly date, Guid? sessionId, DateOnly today, CancellationToken cancellationToken)
    {
        await AttendanceRules.EnsurePointableAsync(dbContext, date, today, cancellationToken);
        var dormitory = await AttendanceRules.GetDormitoryAsync(dbContext, dormitoryId, cancellationToken);
        var session = await AttendanceRules.FindInternSessionAsync(dbContext, sessionId, tracking: false, cancellationToken);

        var boarders = await (from be in AttendanceRules.DormitoryStays(dbContext, dormitoryId).AsNoTracking()
                              join s in dbContext.Students on be.StudentId equals s.Id
                              join bed in dbContext.Beds on be.BedId equals bed.Id
                              join room in dbContext.DormitoryRooms on bed.DormitoryRoomId equals room.Id
                              orderby room.Name, bed.BedNumber
                              select new { be.Id, be.StudentId, s.FullName, s.Matricule, RoomName = room.Name, bed.BedNumber })
            .ToListAsync(cancellationToken);

        var stayIds = boarders.Select(b => b.Id).ToList();
        var studentIds = boarders.Select(b => b.StudentId).ToList();
        var onLeave = await AttendanceRules.OnLeaveAsync(dbContext, stayIds, date, cancellationToken);

        // Une école qui n'a encore aucune session n'a rien pointé : la feuille est vierge.
        var recorded = session is null
            ? new Dictionary<Guid, BoardingAttendance>()
            : (await dbContext.BoardingAttendances.AsNoTracking()
                    .Where(a => a.Date == date && a.SessionId == session.Id && studentIds.Contains(a.StudentId))
                    .ToListAsync(cancellationToken))
                .ToDictionary(a => a.StudentId);

        var lines = boarders.Select(b =>
        {
            var away = onLeave.Contains(b.Id);

            if (recorded.TryGetValue(b.StudentId, out var row))
            {
                return new AttendanceSheetLineDto(b.Id, b.FullName, b.Matricule, b.RoomName, b.BedNumber, away, true, row.IsPresent, row.Note);
            }

            return away
                ? new AttendanceSheetLineDto(b.Id, b.FullName, b.Matricule, b.RoomName, b.BedNumber, true, false, false, AttendanceRules.OnLeaveNote)
                : new AttendanceSheetLineDto(b.Id, b.FullName, b.Matricule, b.RoomName, b.BedNumber, false, false, null, null);
        }).ToList();

        var outsideDays = session is not null && !AttendanceDays.Includes(session.Days, date);
        return new AttendanceSheetDto(date, dormitoryId, dormitory.Name, lines, session?.Id, session?.Name, outsideDays);
    }
}
