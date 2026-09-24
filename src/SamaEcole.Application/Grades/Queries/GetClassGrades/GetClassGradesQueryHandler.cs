using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Queries.GetClassGrades;

public class GetClassGradesQueryHandler(IApplicationDbContext dbContext, GradeCorrectionAuthorizer correctionAuthorizer)
    : IRequestHandler<GetClassGradesQuery, IReadOnlyList<StudentGradeRowDto>>
{
    public async Task<IReadOnlyList<StudentGradeRowDto>> Handle(
        GetClassGradesQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : une classe, une
        // matière ou un trimestre d'une autre école y est structurellement introuvable.
        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");
        }

        if (!await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken))
        {
            throw new KeyNotFoundException($"Matière {request.SubjectId} introuvable dans votre établissement.");
        }

        if (!await dbContext.Terms.AnyAsync(t => t.Id == request.TermId, cancellationToken))
        {
            throw new KeyNotFoundException($"Trimestre {request.TermId} introuvable dans votre établissement.");
        }

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName })
            .ToListAsync(cancellationToken);

        // xmin est une colonne système : EF.Property doit s'appliquer à Grade directement, jamais à un
        // type anonyme projeté (même contrainte que GetClassFeesQueryHandler).
        var grades = await dbContext.Grades.AsNoTracking()
            .Where(g => g.SubjectId == request.SubjectId && g.TermId == request.TermId)
            .Select(g => new
            {
                g.Id,
                g.StudentId,
                g.EvaluationType,
                g.Value,
                g.CreatedAt,
                g.CreatedBy,
                RowVersion = EF.Property<uint>(g, "xmin")
            })
            .ToListAsync(cancellationToken);

        // Une seule résolution pour toute la grille : classe, matière et trimestre sont les mêmes
        // pour chaque cellule, donc l'affectation de l'enseignant aussi.
        var scope = await correctionAuthorizer.ResolveAsync(
            request.ClassroomId, request.SubjectId, request.TermId, cancellationToken);

        var byStudent = grades.ToLookup(g => g.StudentId);

        return students
            .Select(s =>
            {
                var devoir1 = byStudent[s.Id].FirstOrDefault(g => g.EvaluationType == EvaluationType.Devoir1);
                var devoir2 = byStudent[s.Id].FirstOrDefault(g => g.EvaluationType == EvaluationType.Devoir2);
                var composition = byStudent[s.Id].FirstOrDefault(g => g.EvaluationType == EvaluationType.Composition);

                return new StudentGradeRowDto(
                    s.Id,
                    s.Matricule,
                    s.FullName,
                    devoir1 is null ? null : new GradeCellDto(devoir1.Id, devoir1.Value, devoir1.RowVersion, scope.CanCorrect(devoir1.CreatedAt, devoir1.CreatedBy)),
                    devoir2 is null ? null : new GradeCellDto(devoir2.Id, devoir2.Value, devoir2.RowVersion, scope.CanCorrect(devoir2.CreatedAt, devoir2.CreatedBy)),
                    composition is null ? null : new GradeCellDto(composition.Id, composition.Value, composition.RowVersion, scope.CanCorrect(composition.CreatedAt, composition.CreatedBy)));
            })
            .ToList();
    }
}
