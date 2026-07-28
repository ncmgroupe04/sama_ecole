namespace SamaEcole.Domain.Common;

/// <summary>
/// Marque une entité comme rattachée à un établissement (multi-tenant).
/// Toute entité qui implémente cette interface DOIT être couverte par :
///   1. un Global Query Filter EF Core sur SchoolId (SamaEcole.Persistence)
///   2. une policy Row-Level Security PostgreSQL équivalente (migration)
/// Les deux, jamais un seul. Voir AGENTS.md règle #2 et docs/Volume_3_DDS.md.
/// </summary>
public interface ITenantEntity
{
    Guid SchoolId { get; set; }
}
