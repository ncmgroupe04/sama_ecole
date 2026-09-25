using SamaEcole.Application.ClassSubjects;
using System.Globalization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;

public class GetGradeSheetPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IGradeSheetPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    SubjectFollowScope followScope)
    : IRequestHandler<GetGradeSheetPdfQuery, GradeSheetPdfResult>
{
    /// <summary>
    /// Tri alphabétique FRANÇAIS, en mémoire : l'ordre du serveur de base dépend de sa collation, qui ne
    /// range pas toujours « Élodie » avant « Emma ». Une fiche que l'on parcourt au stylo, un nom après
    /// l'autre, doit suivre l'ordre auquel l'enseignant s'attend.
    /// </summary>
    private static readonly StringComparer FrenchOrder = StringComparer.Create(new CultureInfo("fr-FR"), ignoreCase: true);

    public async Task<GradeSheetPdfResult> Handle(GetGradeSheetPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : une classe, une
        // matière ou un trimestre d'une autre école y est structurellement introuvable (404).
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        var subject = await dbContext.Subjects.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SubjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Matière {request.SubjectId} introuvable dans votre établissement.");

        var term = await dbContext.Terms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TermId, cancellationToken)
            ?? throw new KeyNotFoundException($"Période {request.TermId} introuvable dans votre établissement.");

        // Un DOMAINE d'une grille APC n'est qu'un regroupement : il ne porte jamais de note (voir
        // CreateGradeCommandHandler) — lui imprimer une fiche de saisie n'aurait aucun sens.
        if (await dbContext.Subjects.AnyAsync(s => s.ParentSubjectId == request.SubjectId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId),
                    "Cette matière est un domaine d'évaluation : imprimez la fiche d'une de ses activités.")
            ]);
        }

        var school = await dbContext.Schools.AsNoTracking()
            .FirstAsync(s => s.Id == schoolId, cancellationToken);

        var schoolYearLabel = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.Id == term.SchoolYearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        // Matière optionnelle (Évolution N°6) : la fiche papier liste les mêmes élèves que la grille de saisie.
        var allowed = await followScope.RestrictedStudentsAsync(
            request.ClassroomId, request.SubjectId, term.SchoolYearId, cancellationToken);

        var students = (await dbContext.Students.AsNoTracking()
                .Where(s => s.ClassroomId == request.ClassroomId)
                .Select(s => new { s.Id, Row = new GradeSheetPdfStudent(s.Matricule, s.FullName) })
                .ToListAsync(cancellationToken))
            .Where(s => allowed is null || allowed.Contains(s.Id))
            .Select(s => s.Row)
            .OrderBy(s => s.FullName, FrenchOrder)
            .ThenBy(s => s.Matricule, StringComparer.Ordinal)
            .ToList();

        // Barème de la MATIÈRE (grilles APC : /40, /60…) et, à défaut, celui du CYCLE de la classe — le
        // même que celui appliqué à la saisie : annoncer « /20 » sur une ligne notée sur 40 tromperait
        // l'enseignant qui note au stylo.
        var cycleScale = GradingScaleGuard.ScaleForCycle(classroom.Cycle);
        var maxScore = await GradingScaleGuard.ResolveMaxScoreAsync(dbContext, request.SubjectId, cycleScale, cancellationToken);

        var evaluationLabel = request.EvaluationType switch
        {
            EvaluationType.Devoir1 => "Devoir 1",
            EvaluationType.Devoir2 => "Devoir 2",
            _ => "Composition"
        };

        var sheet = new GradeSheetPdfDto(
            school.Name, schoolYearLabel, term.Label, classroom.Name, subject.Name, evaluationLabel, maxScore, students);

        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);

        return new GradeSheetPdfResult(
            pdfGenerator.Generate(sheet, logo),
            $"Fiche-Notes-{classroom.Name}-{subject.Name}-{evaluationLabel}.pdf".Replace(' ', '-'));
    }
}
