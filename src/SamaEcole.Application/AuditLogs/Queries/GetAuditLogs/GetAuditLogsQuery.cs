using MediatR;

namespace SamaEcole.Application.AuditLogs.Queries.GetAuditLogs;

/// <summary>
/// GET /audit-logs — ticket JGK-H01. Paginé : un établissement actif accumule vite plus d'entrées
/// qu'une seule page ne peut raisonnablement afficher (même raisonnement que GetStudentsQuery).
/// </summary>
public record GetAuditLogsQuery : IRequest<PaginatedAuditLogs>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>Filtre optionnel sur le domaine fonctionnel (ex. "Finance", "Users").</summary>
    public string? Module { get; init; }

    /// <summary>Filtre optionnel sur le résultat : true = succès uniquement, false = échecs uniquement, null = tous.</summary>
    public bool? Success { get; init; }
}

public record AuditLogEntry(
    Guid Id,
    string ActorFullName,
    string Module,
    string Action,
    bool Success,
    string? FailureReason,
    string? IpAddress,
    DateTimeOffset OccurredAt);

public record PaginatedAuditLogs(IReadOnlyList<AuditLogEntry> Items, int TotalCount, int Page, int PageSize);
