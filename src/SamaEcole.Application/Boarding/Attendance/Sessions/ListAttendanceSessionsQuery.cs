using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>
/// GET /api/v1/boarding/attendance/sessions — les sessions de pointage de l'école, dans l'ordre choisi par la Direction.
/// <see cref="ActiveOnly"/> : les écrans de pointage ne proposent que les sessions actives ; l'écran de configuration les liste
/// toutes. <see cref="Audience"/> : l'Oustaz ne voit que les sessions de Halqa.
/// </summary>
public record ListAttendanceSessionsQuery(bool ActiveOnly = false, AttendanceAudience? Audience = null)
    : IRequest<IReadOnlyList<AttendanceSessionDto>>;

public class ListAttendanceSessionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListAttendanceSessionsQuery, IReadOnlyList<AttendanceSessionDto>>
{
    public async Task<IReadOnlyList<AttendanceSessionDto>> Handle(
        ListAttendanceSessionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.AttendanceSessions.AsNoTracking();
        if (request.ActiveOnly)
        {
            query = query.Where(s => s.IsActive);
        }

        if (request.Audience is { } audience)
        {
            query = query.Where(s => s.Audience == audience);
        }

        var sessions = await query.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(cancellationToken);

        var ids = sessions.Select(s => s.Id).ToList();
        var used = (await dbContext.BoardingAttendances.AsNoTracking()
                .Where(a => ids.Contains(a.SessionId))
                .Select(a => a.SessionId).Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return sessions.Select(s => AttendanceSessionMapper.ToDto(s, used.Contains(s.Id))).ToList();
    }
}

/// <summary>GET /api/v1/boarding/attendance/session-templates — les modèles proposés pour créer une session en un clic.</summary>
public record ListAttendanceSessionTemplatesQuery : IRequest<IReadOnlyList<AttendanceSessionTemplateDto>>;

public class ListAttendanceSessionTemplatesQueryHandler
    : IRequestHandler<ListAttendanceSessionTemplatesQuery, IReadOnlyList<AttendanceSessionTemplateDto>>
{
    public Task<IReadOnlyList<AttendanceSessionTemplateDto>> Handle(
        ListAttendanceSessionTemplatesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(AttendanceSessionTemplates.All);
}
