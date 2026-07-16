using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.SaveGrade;

public class SaveGradeCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<SaveGradeCommand, SaveGradeResult>
{
    public async Task<SaveGradeResult> Handle(SaveGradeCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Élève, matière et trimestre doivent exister DANS CETTE ÉCOLE. Le Global Query Filter
        // restreint déjà chaque requête au tenant courant : une ligne d'une autre école y est
        // introuvable, et ce contrôle vaut vérification d'appartenance autant que d'existence — sans
        // lui, la FK composite rejetterait la ligne, mais sous la forme d'une DbUpdateException
        // remontée en 500 plutôt qu'une erreur de saisie exploitable sur le bon champ.
        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "L'élève indiqué n'existe pas dans votre établissement.")
            ]);
        }

        if (!await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);
        }

        if (!await dbContext.Terms.AnyAsync(t => t.Id == request.TermId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.TermId), "Le trimestre indiqué n'existe pas dans votre établissement.")
            ]);
        }

        // Barème de l'école (10 ou 20, Volume 1 §8.2) : une note hors plage n'est pas une erreur de
        // concurrence, mais une erreur de saisie sur le bon champ (422, pas 500).
        var gradingScale = (await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))
            ?.GradingScale ?? SchoolSettingsDefaults.GradingScale;

        if (request.Value > gradingScale)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Value), $"La note ne peut pas dépasser le barème de l'école ({gradingScale}).")
            ]);
        }

        if (request.RowVersion is { } expectedVersion)
        {
            // Correction d'une note existante : verrouillage optimiste xmin (AGENTS.md règle #5), comme
            // UpdateClassFeeCommandHandler. Un jeton périmé fait échouer SaveChangesAsync en 409, jamais
            // un écrasement silencieux du travail d'un autre enseignant.
            var grade = await dbContext.Grades.FirstOrDefaultAsync(g =>
                    g.StudentId == request.StudentId && g.SubjectId == request.SubjectId
                    && g.TermId == request.TermId && g.EvaluationType == request.EvaluationType,
                cancellationToken)
                ?? throw new KeyNotFoundException("Aucune note existante à corriger pour cette clé.");

            dbContext.SetOriginalConcurrencyToken(grade, expectedVersion);
            grade.Value = request.Value;

            await dbContext.SaveChangesAsync(cancellationToken);

            var updatedRowVersion = await dbContext.Grades.AsNoTracking()
                .Where(g => g.Id == grade.Id)
                .Select(g => EF.Property<uint>(g, "xmin"))
                .FirstAsync(cancellationToken);

            return new SaveGradeResult(grade.Id, grade.Value, updatedRowVersion);
        }

        // Première saisie : aucun jeton à comparer. Si une note existe déjà pour cette clé — créée
        // entre-temps par un autre enseignant, ou simplement déjà présente — l'index unique
        // UX_grades_single_entry rejette l'INSERT, et SaveChangesAsync traduit la violation en
        // ConcurrencyConflictException (409), jamais en doublon silencieux (AGENTS.md règle #5).
        var newGrade = new Grade
        {
            SchoolId = schoolId,
            StudentId = request.StudentId,
            SubjectId = request.SubjectId,
            TermId = request.TermId,
            EvaluationType = request.EvaluationType,
            Value = request.Value
        };

        dbContext.Grades.Add(newGrade);
        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Grades.AsNoTracking()
            .Where(g => g.Id == newGrade.Id)
            .Select(g => EF.Property<uint>(g, "xmin"))
            .FirstAsync(cancellationToken);

        return new SaveGradeResult(newGrade.Id, newGrade.Value, newRowVersion);
    }
}
