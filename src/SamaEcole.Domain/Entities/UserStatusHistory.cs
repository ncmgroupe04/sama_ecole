using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Journal des changements de statut d'un compte (ticket JGK-A05).
///
/// APPEND-ONLY : docs/Volume_7_Security.md §7 impose que ce journal soit « consultable mais jamais
/// modifiable, y compris par un administrateur ». Ce n'est pas qu'une convention de code — la
/// migration RETIRE les droits UPDATE et DELETE au rôle applicatif sur cette table. Même un bug ou
/// une injection SQL ne peut donc pas réécrire l'histoire.
///
/// Aucun soft delete ici, pour la même raison : une ligne d'audit ne se supprime pas.
/// </summary>
public class UserStatusHistory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Compte dont le statut a changé.</summary>
    public Guid UserId { get; set; }

    public EntityStatus PreviousStatus { get; set; }
    public EntityStatus NewStatus { get; set; }

    /// <summary>Motif OBLIGATOIRE (ticket JGK-A05) : un blocage sans justification n'est pas traçable.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Auteur du changement — jamais lu depuis la requête, toujours depuis le JWT.</summary>
    public Guid ChangedByUserId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
