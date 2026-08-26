using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Résultat et mention d'un <see cref="ExamDossier"/> à la délibération (Volume 1 §22.6, Volume 3 DDS
/// §5.10). Au plus un résultat par dossier.
/// </summary>
public class ExamResult : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ExamDossierId { get; set; }
    public ExamDossier ExamDossier { get; set; } = null!;

    public bool IsAdmitted { get; set; }

    /// <summary>Nul si non admis, ou si la session est <see cref="Enums.ExamType.CFEE"/> (pas de mention).</summary>
    public ExamMention? Mention { get; set; }

    public decimal? AverageScore { get; set; }

    public DateOnly DeliberatedOn { get; set; }
}
