using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class TaxeDeclaration : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public int Month { get; set; }
    public int Year { get; set; }

    public decimal TotalIpres { get; set; }
    public decimal TotalCss { get; set; }
    public decimal TotalVrs { get; set; }
    public decimal TotalBrs { get; set; }

    public decimal TvaCollected { get; set; }
    public decimal TvaDeductible { get; set; }
    public decimal NetTva { get; set; }

    public decimal TotalDueToState { get; set; }
}
