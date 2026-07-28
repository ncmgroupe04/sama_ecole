using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class FichePaie : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EmployeeContractId { get; set; }
    public EmployeeContract EmployeeContract { get; set; } = null!;

    public int Month { get; set; }
    public int Year { get; set; }

    public decimal HoursWorked { get; set; }
    
    public decimal GrossSalary { get; set; }
    public decimal TransportAllowance { get; set; }
    
    public decimal IpresEmployee { get; set; }
    public decimal IpresEmployer { get; set; }
    
    public decimal CssEmployer { get; set; }
    
    public decimal Vrs { get; set; }
    public decimal Brs { get; set; }
    
    public decimal NetSalary { get; set; }
}
