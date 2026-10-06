using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>Chambre d'un pavillon. Capacité dérivée du nombre de lits (spec N1).</summary>
public class DormitoryRoom : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid DormitoryId { get; set; }

    public required string Name { get; set; }
}
