using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Séjour d'un élève à l'internat, rattaché à UNE inscription (donc à une année scolaire) — spec §3.4.
/// Un seul séjour actif par inscription et un seul séjour actif par lit, garantis par des index uniques partiels.
///
/// <see cref="MedicalNotes"/> est une donnée de santé d'un mineur : visibilité réservée au Directeur et au
/// Surveillant (spec §5.2), jamais journalisée en clair.
/// </summary>
public class BoardingEnrollment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public Guid EnrollmentId { get; set; }

    public BoardingRegime Regime { get; set; }

    /// <summary>Null pour un demi-pensionnaire, un interne en attente d'affectation, ou un séjour clos.</summary>
    public Guid? BedId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    public string? MedicalNotes { get; set; }

    public string? EmergencyContactName { get; set; }

    public string? EmergencyContactPhone { get; set; }

    public List<AllowedExitPerson> AllowedExitPersons { get; set; } = [];
}
