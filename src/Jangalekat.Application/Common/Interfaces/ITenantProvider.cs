namespace Jangalekat.Application.Common.Interfaces;

/// <summary>
/// Résout le SchoolId du tenant courant à partir du claim JWT. Implémenté dans
/// Jangalekat.Infrastructure.Multitenancy. Utilisé par le Global Query Filter EF Core
/// (Jangalekat.Persistence) — voir AGENTS.md règle #2.
/// Ne JAMAIS résoudre le tenant depuis un paramètre de requête modifiable par le client.
/// </summary>
public interface ITenantProvider
{
    Guid? CurrentSchoolId { get; }
}
