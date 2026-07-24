using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class CashierSession : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid CashierId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal? ClosingBalance { get; set; }

    public CashierSessionStatus Status { get; set; } = CashierSessionStatus.Open;

    public User Cashier { get; set; } = null!;
}
