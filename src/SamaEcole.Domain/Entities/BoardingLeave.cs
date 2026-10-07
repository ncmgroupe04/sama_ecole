using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Sortie ou permission d'un pensionnaire. Le statut (Pending/Active/Returned/Overdue) n'est PAS stocké :
/// il se calcule à partir des dates (spec N6).
/// </summary>
public class BoardingLeave : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid BoardingEnrollmentId { get; set; }

    public DateOnly LeaveDate { get; set; }

    public DateOnly ExpectedReturnDate { get; set; }

    public DateOnly? ActualReturnDate { get; set; }

    public BoardingLeaveReason Reason { get; set; }

    public string? ReasonDetail { get; set; }

    public required string AccompaniedBy { get; set; }

    /// <summary>Figé à la déclaration (spec Q5) : l'accompagnateur n'était pas dans la liste des personnes habilitées.</summary>
    public bool IsCompanionUnlisted { get; set; }
}
