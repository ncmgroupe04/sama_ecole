using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.AuditLogs.Queries.GetAuditLogs;

public class GetAuditLogsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetAuditLogsQuery, PaginatedAuditLogs>
{
    public async Task<PaginatedAuditLogs> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        // Global Query Filter + policy RLS cantonnent d'office la lecture à l'école du JWT.
        var query = dbContext.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Module))
        {
            query = query.Where(a => a.Module == request.Module);
        }

        if (request.Success is { } success)
        {
            query = query.Where(a => a.Success == success);
        }

        // Compté AVANT la pagination : c'est le total du filtre, pas le nombre de lignes renvoyées.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await (
            from a in query
            join actor in dbContext.Users.AsNoTracking() on a.UserId equals actor.Id
            orderby a.OccurredAt descending
            select new AuditLogEntry(
                a.Id, actor.FullName, a.Module, a.Action, a.Success, a.FailureReason, a.IpAddress, a.OccurredAt))
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedAuditLogs(items, totalCount, request.Page, request.PageSize);
    }
}
