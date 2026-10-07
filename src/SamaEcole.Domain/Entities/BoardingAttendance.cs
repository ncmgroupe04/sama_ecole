using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>Pointage de nuit d'un pensionnaire (V1 : nuitées uniquement, spec Q4).</summary>
public class BoardingAttendance : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid BoardingEnrollmentId { get; set; }

    public DateOnly Date { get; set; }

    public bool IsPresent { get; set; }

    public string? Note { get; set; }
}
