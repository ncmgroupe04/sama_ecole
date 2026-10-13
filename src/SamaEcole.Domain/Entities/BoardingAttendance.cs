using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Pointage d'un élève pour une <see cref="AttendanceSession"/> un jour donné (« Appel de nuit » à l'origine, aujourd'hui aussi les
/// repas ou l'appel d'une Halqa). Un seul pointage par élève, session et jour (index unique partiel).
/// </summary>
public class BoardingAttendance : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SessionId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Séjour du pensionnaire pointé. Nul pour une session de Halqa, qui ne passe pas par un séjour.</summary>
    public Guid? BoardingEnrollmentId { get; set; }

    public DateOnly Date { get; set; }

    public bool IsPresent { get; set; }

    public string? Note { get; set; }
}
