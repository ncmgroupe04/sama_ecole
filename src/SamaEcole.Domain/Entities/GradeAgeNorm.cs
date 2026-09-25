using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Tranche d'âge normale d'un NIVEAU (CI, 6e, Terminale…) propre à un établissement (Évolution N°7, cartographie
/// IEF). N'existe que si l'école s'écarte du modèle national codé (AgeNormTemplates) : sans ligne, c'est le modèle
/// qui s'applique. Âges révolus à la date de référence de l'année (31 décembre de l'année de rentrée).
/// </summary>
public class GradeAgeNorm : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Libellé du niveau, dans la nomenclature de ClassroomGradeLevels (« CI », « Sixième », « Terminale »…).</summary>
    public required string GradeLevel { get; set; }

    public int MinAge { get; set; }

    public int MaxAge { get; set; }
}
