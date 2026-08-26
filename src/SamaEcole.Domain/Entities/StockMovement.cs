using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une ligne du journal de stock — APPEND-ONLY, au même titre que <see cref="FeeChangeHistory"/> :
/// la migration n'accorde que SELECT et INSERT au rôle applicatif, et aucun endpoint n'expose de
/// modification ni de suppression. Une saisie erronée se corrige par un mouvement inverse, jamais par
/// une rature — c'est ce qui rend l'inventaire opposable lors d'un contrôle de l'IEF ou de la mairie.
///
/// Aucun verrou optimiste xmin ici, volontairement : une ligne jamais modifiée n'a rien à verrouiller.
/// Le verrou vit sur <see cref="InventoryItem"/>, dont les compteurs, eux, sont mis à jour.
/// </summary>
public class StockMovement : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ItemId { get; set; }

    public InventoryItem? Item { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Toujours strictement positif — le sens vient du <see cref="Type"/> (voir <see cref="StockMovementType"/>).</summary>
    public int Quantity { get; set; }

    public DateOnly MovementDate { get; set; }

    /// <summary>Motif obligatoire : « dotation mairie 2026 », « casse salle 102 », « remise de manuels 6e A ».</summary>
    public required string Reason { get; set; }

    /// <summary>Fournisseur en entrée, destinataire en sortie. Texte libre : un fournisseur n'est pas une entité de la plateforme.</summary>
    public string? CounterpartyLabel { get; set; }

    /// <summary>Renseigné pour les mouvements <c>Attribution</c>/<c>Restitution</c> : la fiche de prêt à l'origine du mouvement.</summary>
    public Guid? AssignmentId { get; set; }

    /// <summary>
    /// Instantanés des compteurs du lot APRÈS application du mouvement. Redondants avec un rejeu du
    /// journal, et c'est le but : un contrôle doit pouvoir lire une ligne isolée sans recalculer
    /// l'historique complet — même raison que <c>PaymentBreakdown</c> pour la caisse.
    /// </summary>
    public int QuantityTotalAfter { get; set; }

    public int QuantityAvailableAfter { get; set; }
}
