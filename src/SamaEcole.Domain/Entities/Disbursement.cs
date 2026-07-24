using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class Disbursement : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    
    public string Reason { get; set; } = string.Empty;
    public DisbursementCategory Category { get; set; }
    public decimal Amount { get; set; }
    
    public PaymentMethod PaymentMethod { get; set; }
    public DateOnly Date { get; set; }
    public string Beneficiary { get; set; } = string.Empty;
    public string? ReceiptUrl { get; set; }
}
