using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class ScheduleSlot : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    
    public Guid TeacherId { get; set; }
    public Guid ClassroomId { get; set; }
    public Guid SubjectId { get; set; }
    
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    
    public string? RoomNumber { get; set; }
    public bool IsTeacherSubmitted { get; set; }

    // Navigation properties
    public Teacher Teacher { get; set; } = null!;
    public Classroom Classroom { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}
