using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Heures effectuées un jour donné par un enseignant Vacataire, pour justifier le calcul de son
/// bulletin de paie horaire (<see cref="EmployeeContract.HourlyRate"/>). Distinct de
/// <c>PayslipDto.HoursWorked</c> (un total agrégé saisi à la génération de la fiche) : cette table
/// est le détail jour par jour qui permet d'imprimer une fiche de suivi vérifiable, pas une
/// alimentation automatique du calcul de paie (hors périmètre de ce ticket — voir cahier des charges
/// Documents administratifs).
/// </summary>
public class TeacherHourRecord : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid EmployeeContractId { get; set; }
    public EmployeeContract EmployeeContract { get; set; } = null!;

    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string? Note { get; set; }
}
