using MediatR;

namespace SamaEcole.Application.Grades.Queries.GetGradeSummary;

/// <summary>
/// GET /api/v1/grades/calculate?studentId=&amp;termId= — ticket JGK-G02. Calcule, pour un élève et un
/// trimestre : la moyenne de chaque matière notée, la moyenne générale pondérée par les coefficients
/// (Volume 1 §8.3 : « total des coefficients, total des points, moyenne générale »), et la mention qui
/// en découle (§8.4). Lecture seule, aucune écriture — un recalcul à la demande, jamais persisté ici :
/// la persistance (bulletin figé) est l'affaire de JGK-G03.
/// </summary>
public record GetGradeSummaryQuery(Guid StudentId, Guid TermId) : IRequest<GradeSummaryDto>;

/// <summary>
/// Moyenne d'une matière pour ce trimestre. <see cref="Devoir"/>/<see cref="Composition"/> sont null si
/// ce type d'évaluation n'a pas encore été saisi — la moyenne se calcule alors sur ce qui existe, sans
/// exiger que les deux soient renseignés (la saisie progresse au fil du trimestre).
/// </summary>
public record SubjectGradeDto(
    Guid SubjectId,
    string SubjectName,
    decimal? Devoir,
    decimal? Composition,
    decimal Average,
    decimal Coefficient,
    decimal WeightedPoints);

/// <summary>
/// <see cref="Mention"/> est null tant qu'aucune matière n'est notée (rien à qualifier), ou si la
/// moyenne générale n'atteint le seuil d'aucune mention configurée.
/// </summary>
public record GradeSummaryDto(
    Guid StudentId,
    Guid TermId,
    IReadOnlyList<SubjectGradeDto> Subjects,
    decimal TotalCoefficients,
    decimal TotalPoints,
    decimal GeneralAverage,
    string? Mention);
