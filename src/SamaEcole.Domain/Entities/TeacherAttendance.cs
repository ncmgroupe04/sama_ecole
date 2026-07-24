using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class TeacherAttendance : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid TeacherId { get; set; }
    public Teacher Teacher { get; set; } = null!;
    
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public int LateMinutes { get; set; }
    public string? Reason { get; set; }
}
