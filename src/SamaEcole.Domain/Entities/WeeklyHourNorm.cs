using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Volume horaire hebdomadaire d'une matière pour un NIVEAU (et, au lycée, une SÉRIE) propre à un établissement
/// (Évolution N°7). N'existe que si l'école s'écarte du volume de référence codé (WeeklyHourTemplates) : sans ligne,
/// c'est le modèle qui s'applique. Sert au contrôle de conformité des emplois du temps.
/// </summary>
public class WeeklyHourNorm : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Libellé du niveau, dans la nomenclature de ClassroomGradeLevels (« Sixième », « Terminale »…).</summary>
    public required string GradeLevel { get; set; }

    /// <summary>Code de série (LyceeSeries) ; null : toutes les classes du niveau.</summary>
    public string? Series { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>Heures par semaine (0 : matière non enseignée à ce niveau).</summary>
    public decimal WeeklyHours { get; set; }
}
