namespace SamaEcole.Application.Internat;

/// <summary>Un point de la courbe d'un élève : son total de quarts à la FIN d'une journée (la dernière saisie du jour fait foi).</summary>
public record HizbHistoryPointDto(DateOnly Date, int TotalQuarters, decimal ProgressPercent);

/// <summary>Une évaluation du journal. <paramref name="EvaluatedBy"/> est nul si le compte n'existe plus ou pour une ligne de départ.</summary>
public record HizbEvaluationEntryDto(
    DateTimeOffset EvaluatedAt,
    int HizbNumber,
    int QuartersBefore,
    int QuartersAfter,
    int? Rating,
    int TotalQuartersAfter,
    bool IsBaseline,
    string? EvaluatedBy);

/// <summary>
/// Historique d'un élève sur une période. <paramref name="OpeningTotalQuarters"/> est le total AU DÉBUT de la période (la dernière
/// saisie antérieure), pour que la courbe démarre au bon niveau ; <c>null</c> quand la période est ouverte vers le passé.
/// <paramref name="FirstEvaluationAt"/> : date de la toute première ligne du journal — avant elle, aucun historique n'existe
/// (il commence au déploiement du journal), et l'écran doit le dire plutôt que de tracer un zéro inventé.
/// </summary>
public record StudentHizbHistoryDto(
    Guid StudentId,
    string FullName,
    DateOnly? From,
    DateOnly? To,
    int CurrentTotalQuarters,
    decimal CurrentProgressPercent,
    int? OpeningTotalQuarters,
    DateTimeOffset? FirstEvaluationAt,
    IReadOnlyList<HizbHistoryPointDto> Points,
    IReadOnlyList<HizbEvaluationEntryDto> Recent);

/// <summary>Progression moyenne d'un mois : moyenne, sur les élèves du périmètre, du total de quarts à la fin du mois (sur 240).</summary>
public record ProgressMonthDto(DateOnly MonthStart, decimal AverageProgressPercent, int EvaluationCount, int StudentsEvaluated);

/// <summary>
/// Progression mensuelle d'une école ou d'une Halqa. Ne contient que les mois à partir du premier enregistrement du journal :
/// avant, il n'y a pas d'historique, et une moyenne à 0 % serait inventée.
/// </summary>
public record ProgressHistoryDto(
    Guid? InstructorId,
    int StudentsInScope,
    DateTimeOffset? FirstEvaluationAt,
    IReadOnlyList<ProgressMonthDto> Months);
