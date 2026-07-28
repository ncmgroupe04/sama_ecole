using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.CreateGrade;

public class CreateGradeCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateGradeCommand, GradeResult>
{
    public async Task<GradeResult> Handle(CreateGradeCommand request, CancellationToken cancellationToken)
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

        // Barème du CYCLE de la classe de l'élève (Primaire /10, Collège & Lycée /20) — backstop du
        // contrôle déjà tenu par CreateGradeCommandValidator, sur la même base pour ne pas le contredire.
        var gradingScale = await GradingScaleGuard.ResolveScaleForStudentAsync(dbContext, request.StudentId, cancellationToken);
        GradingScaleGuard.EnsureWithinScale(request.Value, gradingScale, nameof(request.Value));

        // Aucune note ne doit déjà exister pour cette clé : l'index unique UX_grades_single_entry
        // rejette l'INSERT sinon (une autre création concurrente, ou une note déjà là), et
        // SaveChangesAsync traduit la violation en ConcurrencyConflictException (409), jamais en
        // doublon silencieux (AGENTS.md règle #5).
        var grade = new Grade
        {
            SchoolId = schoolId,
            StudentId = request.StudentId,
            SubjectId = request.SubjectId,
            TermId = request.TermId,
            EvaluationType = request.EvaluationType,
            Value = request.Value
        };

        dbContext.Grades.Add(grade);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.Grades.AsNoTracking()
            .Where(g => g.Id == grade.Id)
            .Select(g => EF.Property<uint>(g, "xmin"))
            .FirstAsync(cancellationToken);

        return new GradeResult(grade.Id, grade.Value, rowVersion);
    }
}
