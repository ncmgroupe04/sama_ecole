using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Platform.Queries.GetPlatformActivity;

/// <summary>
/// GET /admin/platform/activity — réservé au Super Admin (console plateforme). Journal d'audit de
/// TOUTES les écoles, trié du plus récent au plus ancien, paginé. `audit_logs` reste une table tenant
/// normale (RLS + Global Query Filter, AGENTS.md règle #2) : c'est la fonction SECURITY DEFINER
/// `get_global_audit_logs` (migration AddPlatformAdminViews), pas cette table, qui contourne
/// l'isolation — de façon auditée et volontaire, réservée à ce seul rôle.
/// </summary>
public record GetPlatformActivityQuery : IRequest<PaginatedGlobalAuditLogs>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public record GlobalAuditLogItem(
    Guid Id,
    Guid SchoolId,
    string SchoolName,
    Guid UserId,
    string ActorFullName,
    string Module,
    string Action,
    bool Success,
    string? FailureReason,
    string? IpAddress,
    DateTimeOffset OccurredAt);

public record PaginatedGlobalAuditLogs(IReadOnlyList<GlobalAuditLogItem> Items, int TotalCount, int Page, int PageSize);

public class GetPlatformActivityQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPlatformActivityQuery, PaginatedGlobalAuditLogs>
{
    public async Task<PaginatedGlobalAuditLogs> Handle(
        GetPlatformActivityQuery request,
        CancellationToken cancellationToken)
    {
        var offset = (request.Page - 1) * request.PageSize;

        var rows = await dbContext.GetGlobalAuditLogsAsync(request.PageSize, offset, cancellationToken);

        // Le total (COUNT(*) OVER() côté SQL) voyage sur chaque ligne : à 0 lignes renvoyées (page
        // au-delà du dernier total, ou aucune entrée du tout), on ne peut pas le lire — 0 est alors la
        // valeur correcte dans le second cas, et une limite acceptée dans le premier (cas rare : taper
        // un numéro de page hors plage).
        var totalCount = rows.Count > 0 ? rows[0].TotalCount : 0;

        var items = rows
            .Select(r => new GlobalAuditLogItem(
                r.Id, r.SchoolId, r.SchoolName, r.UserId, r.ActorFullName,
                r.Module, r.Action, r.Success, r.FailureReason, r.IpAddress, r.OccurredAt))
            .ToList();

        return new PaginatedGlobalAuditLogs(items, totalCount, request.Page, request.PageSize);
    }
}
