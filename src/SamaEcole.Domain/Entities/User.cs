using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// SchoolId est nullable uniquement pour un Super Admin (aucun établissement de rattachement).
/// N'implémente pas ITenantEntity pour cette raison : la policy RLS + le filtre applicables à
/// cette table ont leur propre traitement dédié (ticket JGK-A03), différent du mécanisme
/// générique SchoolId non-nul utilisé par les autres entités tenant. Voir docs/Volume_3_DDS.md §5.2.
/// </summary>
public class User : AuditableEntity
{
    public Guid? SchoolId { get; set; }

    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public string? Phone { get; set; }
    public Role Role { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}