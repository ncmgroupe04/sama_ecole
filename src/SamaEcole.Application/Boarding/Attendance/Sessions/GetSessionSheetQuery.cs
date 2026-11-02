using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>
/// GET /api/v1/boarding/attendance/sheet?sessionId=&amp;groupId=&amp;date= (et GET /internat/attendance/sheet pour l'Oustaz) — feuille d'appel
/// d'une session pour un groupe : les pensionnaires d'une classe, ou les élèves d'une Halqa. Un pensionnaire en sortie est pré-rempli
/// « absent — En permission ». Un Oustaz ne lit que sa Halqa (groupe imposé). Mêmes règles de date que le pointage d'un pavillon.
/// </summary>
public record GetSessionSheetQuery(Guid SessionId, Guid? GroupId, DateOnly Date) : IRequest<SessionSheetDto>;

public class GetSessionSheetQueryHandler(
    IApplicationDbContext dbContext, HalqaScopeAuthorizer scope, TimeProvider timeProvider)
    : IRequestHandler<GetSessionSheetQuery, SessionSheetDto>
{
    public async Task<SessionSheetDto> Handle(GetSessionSheetQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var session = await SessionRoster.GetGroupSessionAsync(dbContext, request.SessionId, tracking: false, cancellationToken);
        // Portée AVANT toute lecture : un Oustaz qui vise une autre Halqa reçoit 403, qu'elle existe ou non.
        var groupId = await SessionRoster.ResolveGroupAsync(scope, session, request.GroupId, cancellationToken);
        await AttendanceRules.EnsurePointableAsync(dbContext, request.Date, today, cancellationToken);

        var roster = await SessionRoster.LoadAsync(dbContext, session, groupId, cancellationToken);

        var studentIds = roster.Members.Select(m => m.StudentId).ToList();
        var stayIds = roster.Members.Where(m => m.StayId is not null).Select(m => m.StayId!.Value).ToList();
        var onLeave = await AttendanceRules.OnLeaveAsync(dbContext, stayIds, request.Date, cancellationToken);

        var recorded = (await dbContext.BoardingAttendances.AsNoTracking()
                .Where(a => a.SessionId == session.Id && a.Date == request.Date && studentIds.Contains(a.StudentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.StudentId);

        var lines = roster.Members.Select(m =>
        {
            var away = m.StayId is { } stay && onLeave.Contains(stay);

            if (recorded.TryGetValue(m.StudentId, out var row))
            {
                return new SessionSheetLineDto(m.StudentId, m.FullName, m.FullNameAr, m.Matricule, m.Detail, away, true, row.IsPresent, row.Note);
            }

            return away
                ? new SessionSheetLineDto(m.StudentId, m.FullName, m.FullNameAr, m.Matricule, m.Detail, true, false, false, AttendanceRules.OnLeaveNote)
                : new SessionSheetLineDto(m.StudentId, m.FullName, m.FullNameAr, m.Matricule, m.Detail, false, false, null, null);
        }).ToList();

        return new SessionSheetDto(
            session.Id, session.Name, session.Audience, roster.GroupId, roster.GroupName, request.Date,
            !AttendanceDays.Includes(session.Days, request.Date), lines);
    }
}
