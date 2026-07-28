using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class PaymentBreakdown : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid PaymentId { get; set; }

    public Guid FeeCategoryId { get; set; }

    public decimal AmountAllocated { get; set; }

    public Payment Payment { get; set; } = null!;

    public FeeCategory FeeCategory { get; set; } = null!;
}
