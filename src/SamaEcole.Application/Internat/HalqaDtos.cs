using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Internat;

/// <summary>
/// Un élève de la Halqa d'un Oustaz, avec son indicateur global de mémorisation (quarts acquis sur les 240 du
/// Coran). Les agrégats sont calculés en base : la liste ne charge jamais les 60 lignes de chaque élève.
/// </summary>
public record HalqaStudentDto(
    Guid StudentId,
    string Matricule,
    string FullName,
    string? FullNameAr,
    int CompletedHizbs,
    int InProgressHizbs,
    int CompletedQuarters,
    decimal ProgressPercent,
    DateTimeOffset? LastEvaluatedAt);

public record HalqaDto(
    Guid InstructorId,
    string InstructorName,
    string? InstructorNameAr,
    int StudentCount,
    decimal AverageProgressPercent,
    IReadOnlyList<HalqaStudentDto> Students);

/// <summary>
/// Une case de la grille des 60 Hizb. <see cref="RowVersion"/> est le jeton xmin de la ligne, à renvoyer tel quel à
/// la prochaine écriture (AGENTS.md règle #5) ; il est <c>null</c> tant que le Hizb n'a jamais été saisi — la case
/// « non commencé » d'un élève vierge n'existe pas en base, elle est synthétisée à la lecture.
/// </summary>
public record HizbCellDto(
    int HizbNumber,
    int CompletedQuarters,
    HizbMemorizationState State,
    DateTimeOffset? LastEvaluatedAt,
    int? Rating,
    uint? RowVersion);

public record HizbSummaryDto(
    int CompletedHizbs,
    int InProgressHizbs,
    int CompletedQuarters,
    int TotalQuarters,
    decimal ProgressPercent);

public record StudentHizbProgressDto(
    Guid StudentId,
    string FullName,
    Guid? InstructorId,
    HizbSummaryDto Summary,
    IReadOnlyList<HizbCellDto> Hizbs);
