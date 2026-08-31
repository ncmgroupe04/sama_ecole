namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Écrit une entrée du journal d'audit (JGK-H01). Passe par une fonction PostgreSQL SECURITY DEFINER
/// (append_audit_log, migration AddAuditLogAppendFunction), en une seule instruction ADO.NET brute :
/// exactement comme ISchoolProvisioningStore/IAuthStore contournent la même policy RLS.
///
/// Deux appelants :
///   • les scénarios SANS tenant établi côté session — connexion (JGK-A04, avant authentification) et
///     actions Super Admin (aucun SchoolId propre) — où un INSERT EF classique serait rejeté par la
///     policy RLS de audit_logs ;
///   • AuditLoggingBehavior, y compris le cas nominal (acteur authentifié, SchoolId propre) : l'écriture
///     brute n'appelle jamais SaveChangesAsync, donc elle ne repartage pas le ChangeTracker d'un Handler
///     qui vient d'échouer — ni perte silencieuse de l'entrée d'échec, ni commit d'un état partiel.
/// </summary>
public interface IAuditLogStore
{
    Task AppendAsync(
        Guid schoolId,
        Guid userId,
        string module,
        string action,
        bool success,
        string? failureReason,
        string? ipAddress,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}
