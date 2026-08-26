using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamStatistics;

/// <summary>
/// GET /api/v1/exams/statistics — taux de réussite par série/classe, comparaison interannuelle.
/// Ne porte QUE sur les dossiers <c>Transmis</c> ou <c>Valide</c> (Volume 1 §22.6) : un dossier
/// encore en préparation ne doit jamais fausser un taux affiché en cours d'année.
/// </summary>
public record GetExamStatisticsQuery(Guid? SchoolYearId) : IRequest<ExamStatistics>;

public record ExamStatistics(
    Guid? SchoolYearId,
    IReadOnlyList<ExamStatisticsBySeries> BySeries,
    IReadOnlyList<ExamStatisticsByYear> PreviousYearComparison);

public record ExamStatisticsBySeries(
    ExamType ExamType,
    string? Series,
    int CandidateCount,
    int AdmittedCount,
    double SuccessRate);

public record ExamStatisticsByYear(string SchoolYearLabel, double SuccessRate);
