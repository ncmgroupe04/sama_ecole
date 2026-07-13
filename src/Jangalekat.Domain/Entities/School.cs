using Jangalekat.Domain.Common;
using Jangalekat.Domain.Enums;

namespace Jangalekat.Domain.Entities;

/// <summary>
/// Établissement scolaire = unité d'isolation multi-tenant. Cette entité n'implémente PAS
/// ITenantEntity : elle définit le tenant, elle ne lui appartient pas.
/// Voir docs/Volume_3_DDS.md §Multi-tenant (cas particulier) et docs/ERD.md.
/// </summary>
public class School : AuditableEntity
{
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
