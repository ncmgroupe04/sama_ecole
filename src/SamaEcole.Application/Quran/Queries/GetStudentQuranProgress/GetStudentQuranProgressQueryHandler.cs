using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;

public class GetStudentQuranProgressQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentQuranProgressQuery, IReadOnlyList<QuranProgressDto>>
{
    public async Task<IReadOnlyList<QuranProgressDto>> Handle(
        GetStudentQuranProgressQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => p.StudentId == request.StudentId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new QuranProgressDto(
                p.Id, p.StudentId, p.JuzNumber, p.HizbNumber, p.SurahNumber,
                p.Status, p.EvaluationDate, p.Notes, EF.Property<uint>(p, "xmin")))
            .ToListAsync(cancellationToken);
    }
}
