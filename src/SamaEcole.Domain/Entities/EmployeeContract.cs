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

    /// <summary>
    /// Date de clôture du contrat (Volume 1 §14.1 : « un contrat n'est jamais supprimé physiquement…
    /// il est clôturé à une date, et reste consultable pour l'historique de paie et les attestations »).
    /// Null tant que le contrat est actif. Une fois posée, aucune nouvelle fiche de paie ne peut plus
    /// être générée pour ce contrat (voir GenerateFichePaieCommandHandler) — la dernière fiche due doit
    /// être générée AVANT la clôture. Le contrat lui-même n'est jamais supprimé : il reste lisible pour
    /// l'historique de paie et l'attestation de travail (§14.4).
    /// </summary>
    public DateOnly? EndDate { get; set; }

    public Teacher? Teacher { get; set; }
    public User? User { get; set; }
}
