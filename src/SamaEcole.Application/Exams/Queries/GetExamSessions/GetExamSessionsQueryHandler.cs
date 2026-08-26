using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Commands.CreateExamSession;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamSessions;

public class GetExamSessionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExamSessionsQuery, IReadOnlyList<ExamSessionResult>>
{
    public async Task<IReadOnlyList<ExamSessionResult>> Handle(
        GetExamSessionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ExamSessions.AsNoTracking();

        if (request.SchoolYearId is { } schoolYearId)
        {
            query = query.Where(s => s.SchoolYearId == schoolYearId);
        }

        if (request.ExamType is { } examType)
        {
            query = query.Where(s => s.ExamType == examType);
        }

        return await dbContext.ToListOrEmptyOnMissingTableAsync(
            query
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new ExamSessionResult(
                    s.Id,
                    s.SchoolYearId,
                    s.ExamType,
                    s.Series,
                    s.CenterName,
                    s.Status.ToString(),
                    dbContext.ExamDossiers.Count(d => d.ExamSessionId == s.Id),
                    EF.Property<uint>(s, "xmin"))),
            cancellationToken);
    }
}
