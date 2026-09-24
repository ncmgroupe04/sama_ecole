using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Surcharge du coefficient d'une matière par le Directeur (Évolution N°4), pour UNE année scolaire et
/// UNE portée : une série de lycée (<see cref="Series"/>) OU une classe précise (<see cref="ClassroomId"/>).
/// Exactement l'une des deux — contrainte de base, pas seulement de code.
///
/// Elle ne remplace jamais <see cref="Subject.Coefficient"/> : c'est la valeur de REPLI quand aucune
/// surcharge ne s'applique. Précédence au calcul : classe, puis série, puis matière
/// (SubjectCoefficients.Resolve). Rattachée à l'année pour qu'un changement de coefficient en 2027 ne
/// réécrive pas les bulletins de 2026 (arbitrage A6). Suppression logique uniquement : « Rétablir » la
/// valeur de la matière soft-delete la ligne (règle #6), et la clé redevient libre.
/// </summary>
public class SubjectCoefficientOverride : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SchoolYearId { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>Portée « classe » : la classe visée. Null pour une surcharge de série.</summary>
    public Guid? ClassroomId { get; set; }

    /// <summary>Portée « série » : code du catalogue fermé LyceeSeries (L1, L2, S1, S2, TECH). Null pour une surcharge de classe.</summary>
    public string? Series { get; set; }

    /// <summary>Mêmes bornes que <see cref="Subject.Coefficient"/> : strictement positif, numeric(4,2).</summary>
    public decimal Coefficient { get; set; }
}
