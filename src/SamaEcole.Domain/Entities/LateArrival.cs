using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class LateArrival : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    
    public DateTime Date { get; set; }
    public int Minutes { get; set; }
    public string Reason { get; set; } = null!;
    public string? Observations { get; set; }
}
