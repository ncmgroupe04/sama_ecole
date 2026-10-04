using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Modèle de tenue de l'établissement (module Tenues) : « Polo bleu garçon », « Jupe fille », « Tenue de sport ».
/// Ce n'est ni un lot de stock ni une ligne de frais : il fait le pont entre les deux. Ses tailles
/// (<see cref="UniformVariant"/>) portent le stock — un lot <see cref="InventoryItem"/> par taille — et
/// <see cref="FeeCategoryId"/> désigne le frais optionnel qui le facture à l'inscription (PR #51).
///
/// Le module ne crée aucun second système de stock et n'écrit jamais dans Finance (AGENTS.md règle #4) :
/// il LIT les lignes de frais de l'inscription pour savoir si une tenue a été facturée.
///
/// Verrou optimiste xmin (règle #5) : deux administrateurs qui modifient le même modèle ne s'écrasent pas
/// en silence.
/// </summary>
public class UniformItem : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    public UniformKind Kind { get; set; } = UniformKind.Uniforme;

    /// <summary>Public visé — sert à proposer le bon modèle à un élève, jamais à en interdire un.</summary>
    public UniformGender Gender { get; set; } = UniformGender.Mixte;

    /// <summary>Cycle visé, pour filtrer les propositions ; <c>null</c> = tous les cycles.</summary>
    public CycleType? Cycle { get; set; }

    /// <summary>
    /// Catégorie de frais qui facture ce modèle (un frais optionnel « Uniforme », « Tenue de sport »). <c>null</c> :
    /// le modèle n'est rattaché à aucun frais, donc toute remise est « non facturée » et exige un motif.
    /// </summary>
    public Guid? FeeCategoryId { get; set; }

    /// <summary>
    /// Nombre d'unités couvertes par UNE ligne de frais (un frais « Uniforme » peut couvrir deux polos). Une
    /// dotation au-delà de ce nombre n'est pas facturée et exige un motif. Au moins 1.
    /// </summary>
    public int QuantityIncluded { get; set; } = 1;

    public string? Notes { get; set; }
}
