using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Quran;

/// <summary>
/// Une observation de suivi de mémorisation coranique. Sert À LA FOIS de résultat de
/// Create/UpdateQuranProgressCommand et d'élément de liste (Get*QuranProgressQuery) — pas de type
/// Result séparé (YAGNI, voir spec §3.4).
/// </summary>
public record QuranProgressDto(
    Guid Id,
    Guid StudentId,
    int JuzNumber,
    int HizbNumber,
    int SurahNumber,
    QuranMemorizationStatus Status,
    DateOnly? EvaluationDate,
    string? Notes,
    uint RowVersion);
