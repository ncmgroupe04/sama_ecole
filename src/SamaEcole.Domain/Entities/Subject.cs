using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Matière enseignée, rattachée à un niveau (ticket JGK-C03).
///
/// Le NIVEAU est un texte libre, exactement comme <see cref="Classroom.Level"/> : « Primaire »,
/// « Collège », « Terminale S2 »… Une même matière porte un coefficient DIFFÉRENT selon le niveau —
/// « Mathématiques » pèse 4 au primaire et 6 en série scientifique. C'est pourquoi la matière est
/// (Niveau, Nom) et non le seul Nom : dupliquer « Maths » entre deux niveaux est non seulement permis,
/// c'est le cas normal.
///
/// Le COEFFICIENT pilote le calcul des moyennes et des bulletins (docs/Volume_1_Cahier_des_Charges.md
/// §8.3 : « total des coefficients, total des points, moyenne générale »). Il n'est donc jamais
/// décoratif : une valeur fausse ici fausse tous les bulletins du niveau.
/// </summary>
public class Subject : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    /// <summary>Niveau ou cycle, en texte libre — aligné sur la nomenclature des classes de l'établissement.</summary>
    public required string Level { get; set; }

    /// <summary>Poids de la matière dans la moyenne. Strictement positif, décimal (ex. 1,5 ; 4 ; 6).</summary>
    public decimal Coefficient { get; set; }
}
