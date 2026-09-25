using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Queries.GetGradeSheetExcel;

/// <summary>
/// GET /api/v1/grades/sheet/export — bouton « Télécharger la feuille » de la modale d'import. Génère la
/// même feuille que celle attendue en retour par ImportGradeSheetCommand : Matricule, Nom &amp; Prénom,
/// Devoir 1, Devoir 2, Composition, pré-remplie avec les notes déjà saisies de la classe/matière/
/// trimestre — l'enseignant corrige/complète puis réimporte le même fichier.
/// </summary>
public record GetGradeSheetExcelQuery(Guid ClassroomId, Guid SubjectId, Guid TermId) : IRequest<GradeSheetExcelResult>;

public record GradeSheetExcelResult(byte[] Content, string FileName);

public class GetGradeSheetExcelQueryHandler(
    IApplicationDbContext dbContext, IGradeSheetExcelGenerator generator, SubjectFollowScope followScope)
    : IRequestHandler<GetGradeSheetExcelQuery, GradeSheetExcelResult>
{
    public async Task<GradeSheetExcelResult> Handle(GetGradeSheetExcelQuery request, CancellationToken cancellationToken)
    {
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        if (!await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken))
        {
            throw new KeyNotFoundException($"Matière {request.SubjectId} introuvable dans votre établissement.");
        }

        var schoolYearId = await followScope.SchoolYearOfTermAsync(request.TermId, cancellationToken)
            ?? throw new KeyNotFoundException($"Période {request.TermId} introuvable dans votre établissement.");

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName })
            .ToListAsync(cancellationToken);

        // Matière optionnelle (Évolution N°6) : le modèle Excel liste les mêmes élèves que la grille de saisie.
        var allowed = await followScope.RestrictedStudentsAsync(
            request.ClassroomId, request.SubjectId, schoolYearId, cancellationToken);
        if (allowed is not null)
        {
            students = students.Where(s => allowed.Contains(s.Id)).ToList();
        }

        var grades = await dbContext.Grades.AsNoTracking()
            .Where(g => g.SubjectId == request.SubjectId && g.TermId == request.TermId)
            .Select(g => new { g.StudentId, g.EvaluationType, g.Value })
            .ToListAsync(cancellationToken);

        var byStudent = grades.ToLookup(g => g.StudentId);

        var rows = students
            .Select(s =>
            {
                decimal? Value(EvaluationType type) => byStudent[s.Id]
                    .Where(g => g.EvaluationType == type)
                    .Select(g => (decimal?)g.Value)
                    .FirstOrDefault();

                return new GradeSheetStudentRow(
                    s.Matricule, s.FullName, Value(EvaluationType.Devoir1), Value(EvaluationType.Devoir2), Value(EvaluationType.Composition));
            })
            .ToList();

        // Barème de la MATIÈRE (grilles par compétences : /40, /60, /24…) et, à défaut, celui du cycle
        // de la classe. C'est la borne qu'appliquera la réimportation : annoncer « /20 » sur une ligne
        // notée sur 40 ferait refuser par Excel une note que l'API accepte.
        var cycleScale = GradingScaleGuard.ScaleForCycle(classroom.Cycle);
        var gradingScale = await GradingScaleGuard.ResolveMaxScoreAsync(
            dbContext, request.SubjectId, cycleScale, cancellationToken);
        var content = generator.Generate(rows, gradingScale);

        return new GradeSheetExcelResult(content, $"Feuille-Notes-{classroom.Name}.xlsx");
    }
}
