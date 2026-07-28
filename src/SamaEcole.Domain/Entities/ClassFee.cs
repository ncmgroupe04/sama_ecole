using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Montant d'une catégorie de frais pour UNE classe (ticket JGK-F01) — l'intersection
/// (catégorie × classe). C'est le barème : « la mensualité en CM2 est de 15 000 F ».
///
/// Le paramétrage se fait en deux temps (Volume 1 §7.4) :
///   1. un MONTANT STANDARD appliqué en masse à toutes les classes d'une catégorie ;
///   2. des EXCEPTIONS ajustées classe par classe.
/// Les deux passent par cette même table : une exception n'est qu'une ligne dont le montant diffère
/// du standard. Chaque modification est historisée dans <see cref="FeeChangeHistory"/>.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : le montant est une donnée financière sensible. Deux
/// directeurs qui l'éditent en même temps ne doivent jamais s'écraser en silence — le second reçoit
/// un 409. Le jeton est la colonne système <c>xmin</c> de PostgreSQL, exploitée directement par
/// EF Core (docs/Volume_3_DDS.md §1.3) et configurée dans ApplicationDbContext.
/// </summary>
public class ClassFee : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid FeeCategoryId { get; set; }

    public Guid ClassroomId { get; set; }

    /// <summary>Montant en FCFA. Zéro est permis (frais offert) ; jamais négatif.</summary>
    public decimal Amount { get; set; }
}
