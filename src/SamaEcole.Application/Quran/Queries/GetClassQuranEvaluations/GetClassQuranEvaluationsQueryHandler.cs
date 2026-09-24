using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;

public class GetClassQuranEvaluationsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassQuranEvaluationsQuery, IReadOnlyList<ClassQuranEvaluationRowDto>>
{
    public async Task<IReadOnlyList<ClassQuranEvaluationRowDto>> Handle(
        GetClassQuranEvaluationsQuery request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");
        }

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName })
            .ToListAsync(cancellationToken);

        var studentIds = students.Select(s => s.Id).ToList();

        var evaluations = await dbContext.QuranEvaluations.AsNoTracking()
            .Where(e => studentIds.Contains(e.StudentId))
            .Select(e => new QuranEvaluationDto(
                e.Id, e.StudentId, e.EvaluationDate, e.MemoryMistakes, e.TajwidMistakes,
                e.Hesitations, e.FinalScore, EF.Property<uint>(e, "xmin")))
            .ToListAsync(cancellationToken);

        var byStudent = evaluations.ToLookup(e => e.StudentId);

        return students
            .Select(s => new ClassQuranEvaluationRowDto(s.Id, s.Matricule, s.FullName, byStudent[s.Id].ToList()))
            .ToList();
    }
}
