using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Famille de biens du module Inventaire (Mobilier, Manuels scolaires, Informatique, Tenues…).
/// Volontairement libre et propre à chaque école : une école publique classe du patrimoine d'État,
/// une école privée un parc informatique — figer une nomenclature ici obligerait l'une des deux à
/// ranger ses biens dans des cases qui ne sont pas les siennes (même choix que <see cref="Classroom"/>,
/// dont la nomenclature n'est pas figée non plus).
/// </summary>
public class InventoryCategory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ICollection<InventoryItem> Items { get; set; } = [];
}
