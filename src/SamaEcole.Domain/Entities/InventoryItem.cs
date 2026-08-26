using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Un LOT de biens identiques du patrimoine de l'établissement — pas une unité physique.
///
/// Le suivi à l'unité (un vidéoprojecteur, un PC avec son numéro de série) se fait avec un lot de
/// quantité 1 ; le suivi en masse (200 tables-bancs) avec un lot de quantité 200. Ce choix est ce qui
/// permet aux deux cas d'usage — patrimoine d'État et parc informatique privé — de vivre dans le même
/// modèle : un code-barres et un état par LIGNE n'auraient aucun sens sur 200 tables-bancs, et une
/// table d'unités serait du poids mort pour une école qui gère surtout des lots.
///
/// <see cref="QuantityAvailable"/> n'est JAMAIS écrit par un endpoint : il est maintenu uniquement
/// dans la transaction d'un <see cref="StockMovement"/>, sous verrou optimiste xmin (AGENTS.md
/// règle #5) — même traitement que <c>Enrollment.AmountPaid</c> face à la caisse.
/// </summary>
public class InventoryItem : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Code d'inventaire ou code-barres, en saisie LIBRE et facultative : les écoles publiques
    /// réutilisent le numéro d'immatriculation posé par la mairie/l'État sur le bien, qu'aucune
    /// numérotation générée par la plateforme ne doit écraser. Unique par école lorsqu'il est
    /// renseigné (index unique partiel).
    /// </summary>
    public string? Code { get; set; }

    public Guid CategoryId { get; set; }

    public InventoryCategory? Category { get; set; }

    /// <summary>Effectif du lot présent au patrimoine, prêts compris.</summary>
    public int QuantityTotal { get; set; }

    /// <summary>
    /// Effectif immédiatement disponible = <see cref="QuantityTotal"/> moins ce qui est prêté.
    /// Compteur DÉRIVÉ : voir la remarque de classe. Borné en base par une contrainte CHECK.
    /// </summary>
    public int QuantityAvailable { get; set; }

    /// <summary>État dominant du lot — voir <see cref="ItemCondition"/> pour l'arbitrage.</summary>
    public ItemCondition Condition { get; set; } = ItemCondition.Bon;

    /// <summary>
    /// Salle où le lot est entreposé, quand l'école a saisi ses bâtiments (module Infrastructures).
    /// Facultatif : <see cref="LocationLabel"/> reste utilisable par les écoles qui ne l'ont pas fait.
    /// </summary>
    public Guid? RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>Emplacement en texte libre (« Réserve A », « Magasin ») — un local qui n'est pas une salle de classe.</summary>
    public string? LocationLabel { get; set; }

    /// <summary>
    /// Prix unitaire INDICATIF en FCFA, servant à valoriser la fiche d'inventaire. Sans portée
    /// comptable : ce module ne tient pas d'amortissement et n'alimente aucune écriture financière
    /// (AGENTS.md règle #4 — Finance ne se sert jamais ici).
    /// </summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>
    /// Consommable (craie, ramettes, produits d'entretien) : se distribue et ne revient jamais.
    /// Un consommable ne peut donc pas faire l'objet d'une fiche de prêt — refusé en 422.
    /// </summary>
    public bool IsConsumable { get; set; }

    public string? Notes { get; set; }
}
