using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;

public class GetStudentQuranEvaluationsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentQuranEvaluationsQuery, IReadOnlyList<QuranEvaluationDto>>
{
    public async Task<IReadOnlyList<QuranEvaluationDto>> Handle(
        GetStudentQuranEvaluationsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.QuranEvaluations.AsNoTracking()
            .Where(e => e.StudentId == request.StudentId)
            .OrderBy(e => e.EvaluationDate)
            .Select(e => new QuranEvaluationDto(
                e.Id, e.StudentId, e.EvaluationDate, e.MemoryMistakes, e.TajwidMistakes,
                e.Hesitations, e.FinalScore, EF.Property<uint>(e, "xmin")))
            .ToListAsync(cancellationToken);
    }
}
