namespace SamaEcole.Application.Quran;

/// <summary>
/// Une évaluation orale de récitation coranique. Sert de résultat de Create/UpdateQuranEvaluationCommand
/// ET d'élément de liste — pas de type Result séparé (YAGNI, voir spec §3.4).
/// </summary>
public record QuranEvaluationDto(
    Guid Id,
    Guid StudentId,
    DateOnly EvaluationDate,
    int MemoryMistakes,
    int TajwidMistakes,
    int Hesitations,
    decimal FinalScore,
    uint RowVersion);
