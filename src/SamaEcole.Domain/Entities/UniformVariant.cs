using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une taille d'un modèle de tenue (module Tenues). Le stock n'est PAS ici : il vit sur le lot
/// <see cref="InventoryItem"/> que cette taille désigne, de sorte que <c>StockLedger</c> reste l'unique chemin
/// d'écriture des quantités et que <c>stock_movements</c> reste append-only. Un modèle en six tailles = six
/// lots, créés à la création de la taille.
///
/// La taille est un texte LIBRE (« 10 ans », « M », « 38 ») : les tenues scolaires n'ont pas de nomenclature
/// commune (âge, lettre, pointure), et figer une liste obligerait une école à ranger ses tailles dans des cases
/// qui ne sont pas les siennes.
/// </summary>
public class UniformVariant : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid UniformItemId { get; set; }

    public required string SizeLabel { get; set; }

    /// <summary>Ordre d'affichage des tailles d'un même modèle (« 8 ans » avant « 10 ans »).</summary>
    public int SortOrder { get; set; }

    /// <summary>Lot d'inventaire qui porte le stock de cette taille.</summary>
    public Guid InventoryItemId { get; set; }
}
