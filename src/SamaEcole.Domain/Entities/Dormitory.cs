using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Pavillon d'internat (spec docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md §3.1).
/// La capacité n'est PAS stockée : elle se dérive du nombre de lits (spec N1).
/// </summary>
public class Dormitory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    public DormitoryGender Gender { get; set; }

    public string? SupervisorName { get; set; }

    public string? SupervisorPhone { get; set; }

    /// <summary>Liaison facultative à un compte Surveillant (spec Q7).</summary>
    public Guid? SupervisorUserId { get; set; }

    public string? Notes { get; set; }
}
