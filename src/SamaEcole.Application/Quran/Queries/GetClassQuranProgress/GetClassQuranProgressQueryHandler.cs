using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranProgress;

public class GetClassQuranProgressQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassQuranProgressQuery, IReadOnlyList<ClassQuranProgressRowDto>>
{
    public async Task<IReadOnlyList<ClassQuranProgressRowDto>> Handle(
        GetClassQuranProgressQuery request, CancellationToken cancellationToken)
    {
        // Global Query Filter + policy RLS : une classe d'une autre école y est introuvable
        // (même idiome que GetClassGradesQueryHandler).
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

        // xmin est une colonne système : EF.Property s'applique à QuranProgress directement, jamais
        // à un type anonyme projeté (même contrainte que GetClassGradesQueryHandler).
        var entries = await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => studentIds.Contains(p.StudentId))
            .Select(p => new QuranProgressDto(
                p.Id, p.StudentId, p.JuzNumber, p.HizbNumber, p.SurahNumber,
                p.Status, p.EvaluationDate, p.Notes, EF.Property<uint>(p, "xmin")))
            .ToListAsync(cancellationToken);

        var byStudent = entries.ToLookup(e => e.StudentId);

        return students
            .Select(s => new ClassQuranProgressRowDto(s.Id, s.Matricule, s.FullName, byStudent[s.Id].ToList()))
            .ToList();
    }
}
