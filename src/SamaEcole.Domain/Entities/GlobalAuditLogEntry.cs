namespace SamaEcole.Domain.Entities;

/// <summary>
/// Ligne renvoyée par la fonction PostgreSQL SECURITY DEFINER <c>get_global_audit_logs(limit, offset)</c> :
/// le journal d'audit de TOUTES les écoles, pour la console Super Admin (docs/Volume_7_Security.md §15).
/// <see cref="AuditLog"/> reste une table tenant normale (RLS + Global Query Filter, AGENTS.md règle #2)
/// — c'est la fonction, pas cette table, qui contourne l'isolation, de façon auditée et volontaire.
/// Entité SANS CLÉ ni table/vue propre : interrogée UNIQUEMENT via FromSqlRaw (jamais
/// <c>Set&lt;GlobalAuditLogEntry&gt;()</c> seul, qui échouerait faute de source mappée).
/// </summary>
public class GlobalAuditLogEntry
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string ActorFullName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Nombre total de lignes correspondant au filtre, AVANT pagination — porté par chaque
    /// ligne via COUNT(*) OVER() côté SQL, pour éviter un second aller-retour dédié au total.</summary>
    public int TotalCount { get; set; }
}
