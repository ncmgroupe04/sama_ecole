using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>Lit nominatif d'une chambre.</summary>
public class Bed : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid DormitoryRoomId { get; set; }

    public int BedNumber { get; set; }

    /// <summary>Jamais <see cref="BedStatus.Occupied"/> en base (CHECK) : l'occupation se déduit du séjour actif.</summary>
    public BedStatus Status { get; set; } = BedStatus.Available;
}
