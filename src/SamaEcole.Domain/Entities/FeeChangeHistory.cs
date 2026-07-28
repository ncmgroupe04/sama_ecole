using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Journal des modifications de barème (ticket JGK-F01, Volume 1 §7.4 : « Tout changement de montant
/// est historisé — ancien montant, nouveau montant, date, utilisateur »).
///
/// APPEND-ONLY, exactement comme <see cref="UserStatusHistory"/> : la migration RETIRE au rôle
/// applicatif les droits UPDATE et DELETE sur cette table. Un barème facture des familles ; son
/// historique doit être opposable, donc impossible à réécrire — même par un bug ou une injection SQL.
///
/// Pas de soft delete : une ligne d'audit ne se supprime pas.
/// </summary>
public class FeeChangeHistory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Ligne de barème concernée (catégorie × classe).</summary>
    public Guid ClassFeeId { get; set; }

    /// <summary>Montant AVANT le changement. Null lors de la toute première définition (création).</summary>
    public decimal? OldAmount { get; set; }

    public decimal NewAmount { get; set; }

    /// <summary>Auteur du changement — jamais lu depuis la requête, toujours depuis le JWT.</summary>
    public Guid ChangedByUserId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
