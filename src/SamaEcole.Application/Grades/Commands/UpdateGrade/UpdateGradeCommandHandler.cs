using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.UpdateGrade;

public class UpdateGradeCommandHandler(IApplicationDbContext dbContext, GradeCorrectionAuthorizer authorizer)
    : IRequestHandler<UpdateGradeCommand, GradeResult>
{
    public async Task<GradeResult> Handle(UpdateGradeCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une note d'une autre école renvoie 404, jamais une modification silencieuse.
        var grade = await dbContext.Grades
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Note {request.Id} introuvable.");

        // Autorisation (403) AVANT toute validation métier (422) : un appelant qui n'a pas le droit de
        // corriger cette note ne doit rien apprendre des règles de barème par la forme de la réponse.
        // Directeur/Secrétariat passent toujours ; l'Enseignant est borné par la fenêtre de correction
        // de l'école ET par la propriété de la note (voir GradeEditPolicy).
        (await authorizer.ResolveForGradeAsync(grade, cancellationToken)).EnsureCanCorrect(grade);

        // Barème de la LIGNE d'évaluation : celui fixé sur la matière (grilles APC : /40, /60, /24, /16…)
        // et, à défaut, celui du CYCLE de la classe de l'élève (Primaire /10, Collège & Lycée /20) —
        // même résolution qu'à la saisie, sans quoi une note acceptée à la création serait refusée à la
        // correction. Backstop du contrôle déjà tenu par UpdateGradeCommandValidator.
        var cycleScale = await GradingScaleGuard.ResolveScaleForStudentAsync(dbContext, grade.StudentId, cancellationToken);
        var maxScore = await GradingScaleGuard.ResolveMaxScoreAsync(dbContext, grade.SubjectId, cycleScale, cancellationToken);
        GradingScaleGuard.EnsureWithinScale(request.Value, maxScore, nameof(request.Value));

        // Valeur inchangée : ne rien écrire, rien à arbitrer par le verrou optimiste.
        if (grade.Value == request.Value)
        {
            return new GradeResult(grade.Id, grade.Value, request.RowVersion);
        }

        // Cœur du verrou optimiste (AGENTS.md règle #5) : le jeton LU PAR LE CLIENT devient la valeur
        // d'origine imposée à EF. Si la note a changé en base depuis sa lecture, l'UPDATE
        // « WHERE xmin = <jeton client> » ne touche aucune ligne et SaveChangesAsync refuse en 409.
        dbContext.SetOriginalConcurrencyToken(grade, request.RowVersion);
        grade.Value = request.Value;

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Grades.AsNoTracking()
            .Where(g => g.Id == grade.Id)
            .Select(g => EF.Property<uint>(g, "xmin"))
            .FirstAsync(cancellationToken);

        return new GradeResult(grade.Id, grade.Value, newRowVersion);
    }
}
