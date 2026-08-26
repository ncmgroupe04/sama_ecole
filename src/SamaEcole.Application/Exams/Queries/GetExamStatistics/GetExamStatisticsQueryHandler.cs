using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamStatistics;

public class GetExamStatisticsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExamStatisticsQuery, ExamStatistics>
{
    private record StatsRow(Guid SchoolYearId, string SchoolYearLabel, ExamType ExamType, string? Series, bool IsAdmitted);

    public async Task<ExamStatistics> Handle(GetExamStatisticsQuery request, CancellationToken cancellationToken)
    {
        // Groupements en mémoire, après matérialisation : la volumétrie par école (des centaines de
        // candidats, pas des millions) rend ce choix plus lisible qu'un GROUP BY EF Core traduit sur
        // trois tables jointes, pour un gain de performance qui ne se mesurerait pas ici.
        var rows = await dbContext.ToListOrEmptyOnMissingTableAsync(
            dbContext.ExamDossiers.AsNoTracking()
                .Where(d => d.Status == ExamDossierStatus.Transmis || d.Status == ExamDossierStatus.Valide)
                .Select(d => new StatsRow(
                    d.ExamSession.SchoolYearId,
                    d.ExamSession.SchoolYear.Label,
                    d.ExamSession.ExamType,
                    d.ExamSession.Series,
                    d.Result != null && d.Result.IsAdmitted)),
            cancellationToken);

        var bySeries = rows
            .Where(r => request.SchoolYearId == null || r.SchoolYearId == request.SchoolYearId)
            .GroupBy(r => (r.ExamType, r.Series))
            .Select(g => new ExamStatisticsBySeries(
                g.Key.ExamType,
                g.Key.Series,
                g.Count(),
                g.Count(r => r.IsAdmitted),
                g.Count() == 0 ? 0 : (double)g.Count(r => r.IsAdmitted) / g.Count()))
            .OrderBy(s => s.ExamType).ThenBy(s => s.Series)
            .ToList();

        var previousYearComparison = rows
            .GroupBy(r => (r.SchoolYearId, r.SchoolYearLabel))
            .Select(g => new ExamStatisticsByYear(
                g.Key.SchoolYearLabel,
                g.Count() == 0 ? 0 : (double)g.Count(r => r.IsAdmitted) / g.Count()))
            .OrderByDescending(y => y.SchoolYearLabel)
            .ToList();

        return new ExamStatistics(request.SchoolYearId, bySeries, previousYearComparison);
    }
}
