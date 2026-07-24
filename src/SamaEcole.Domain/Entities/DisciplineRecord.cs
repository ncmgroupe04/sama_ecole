using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class DisciplineRecord : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    
    public DateTime Date { get; set; }
    public DisciplineType Type { get; set; }
    public string Reason { get; set; } = null!;
}
