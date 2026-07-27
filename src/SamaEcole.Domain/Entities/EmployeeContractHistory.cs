using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Journal des changements d'un contrat (Volume 1 §14.1 : rémunération modifiée, ou contrat clôturé).
///
/// APPEND-ONLY, exactement comme <see cref="UserStatusHistory"/>/<see cref="FeeChangeHistory"/> : la
/// migration RETIRE au rôle applicatif les droits UPDATE et DELETE. Un contrat engage un salaire ; son
/// historique doit être opposable, donc impossible à réécrire — même par un bug ou une injection SQL.
///
/// Pas de soft delete ici, pour la même raison : une ligne d'audit ne se supprime pas.
/// </summary>
public class EmployeeContractHistory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EmployeeContractId { get; set; }
    public EmployeeContract EmployeeContract { get; set; } = null!;

    public EmployeeContractChangeType ChangeType { get; set; }

    /// <summary>Termes AVANT le changement (toujours renseignés, y compris pour une clôture — les termes ne changent alors pas).</summary>
    public decimal PreviousBaseSalary { get; set; }
    public decimal PreviousHourlyRate { get; set; }
    public decimal PreviousTransportAllowance { get; set; }

    /// <summary>Termes APRÈS le changement — identiques aux précédents si <see cref="ChangeType"/> est <see cref="EmployeeContractChangeType.Closed"/>.</summary>
    public decimal NewBaseSalary { get; set; }
    public decimal NewHourlyRate { get; set; }
    public decimal NewTransportAllowance { get; set; }

    /// <summary>Renseigné uniquement pour <see cref="EmployeeContractChangeType.Closed"/>.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Motif OBLIGATOIRE, même exigence que ChangeUserStatusCommand (JGK-A05) : un changement de salaire ou une clôture sans justification n'est pas traçable.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Auteur du changement — jamais lu depuis la requête, toujours depuis le JWT.</summary>
    public Guid ChangedByUserId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
