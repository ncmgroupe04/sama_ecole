using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class EmployeeContract : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid? TeacherId { get; set; }
    public Guid? UserId { get; set; }

    public ContractType Type { get; set; }

    public decimal BaseSalary { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal TransportAllowance { get; set; }

    public Teacher? Teacher { get; set; }
    public User? User { get; set; }
}
