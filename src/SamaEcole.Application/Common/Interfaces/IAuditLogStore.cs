namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Écrit une entrée du journal d'audit (JGK-H01) pour les scénarios où l'acteur n'a pas de tenant
/// établi côté session — connexion (JGK-A04, avant authentification) et actions Super Admin (aucun
/// SchoolId propre). Passe par une fonction PostgreSQL SECURITY DEFINER (append_audit_log, migration
/// AddAuditLogAppendFunction), exactement comme ISchoolProvisioningStore/IAuthStore contournent la
/// même policy RLS pour la même raison.
///
/// Pour tout le reste (un acteur déjà authentifié, avec SON PROPRE SchoolId), AuditLoggingBehavior
/// suffit : un INSERT EF normal satisfait la policy RLS sans qu'il soit besoin de la contourner.
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
