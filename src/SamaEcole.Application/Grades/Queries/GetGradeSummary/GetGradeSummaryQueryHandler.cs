using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Queries.GetGradeSummary;

public class GetGradeSummaryQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetGradeSummaryQuery, GradeSummaryDto>
{
    public async Task<GradeSummaryDto> Handle(GetGradeSummaryQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : un élève ou un
        // trimestre d'une autre école y est structurellement introuvable.
        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new KeyNotFoundException($"Élève {request.StudentId} introuvable dans votre établissement.");
        }

        if (!await dbContext.Terms.AnyAsync(t => t.Id == request.TermId, cancellationToken))
        {
            throw new KeyNotFoundException($"Trimestre {request.TermId} introuvable dans votre établissement.");
        }

        var rows = await (
            from g in dbContext.Grades.AsNoTracking()
            join s in dbContext.Subjects.AsNoTracking() on g.SubjectId equals s.Id
            where g.StudentId == request.StudentId && g.TermId == request.TermId
            select new { g.SubjectId, s.Name, s.Coefficient, g.EvaluationType, g.Value }
            ).ToListAsync(cancellationToken);

        // Moyenne d'une matière : sur ce qui a été saisi (Devoir seul, Composition seule, ou les deux) —
        // la saisie progresse au fil du trimestre, exiger les deux figerait l'écran tant qu'il manque
        // une note (Volume 1 §8.3 : « total des coefficients, total des points, moyenne générale »).
        // Formule PARTAGÉE avec GetStudentDetailQueryHandler (JGK-D02) via GradeCalculator — une seule
        // source de vérité pour la moyenne d'une matière et la moyenne générale pondérée.
        var subjects = rows
            .GroupBy(r => new { r.SubjectId, r.Name, r.Coefficient })
            .Select(g =>
            {
                var devoir = g.Where(r => r.EvaluationType == EvaluationType.Devoir).Select(r => (decimal?)r.Value).FirstOrDefault();
                var composition = g.Where(r => r.EvaluationType == EvaluationType.Composition).Select(r => (decimal?)r.Value).FirstOrDefault();

                // Toujours non-null : le groupe vient d'au moins une ligne de note (Devoir ou Composition).
                var average = GradeCalculator.SubjectAverage(devoir, composition)!.Value;

                return new SubjectGradeDto(
                    g.Key.SubjectId,
                    g.Key.Name,
                    devoir,
                    composition,
                    average,
                    g.Key.Coefficient,
                    average * g.Key.Coefficient);
            })
            .OrderBy(s => s.SubjectName)
            .ToList();

        var (totalCoefficients, totalPoints, generalAverageOrNull) =
            GradeCalculator.WeightedGeneralAverage(subjects.Select(s => ((decimal?)s.Average, s.Coefficient)));
        var generalAverage = generalAverageOrNull ?? 0m;

        // Aucune matière notée : rien à qualifier, jamais une mention par défaut trompeuse.
        string? mention = null;
        if (totalCoefficients > 0)
        {
            var scale = await MentionScale.ResolveAsync(dbContext, cancellationToken);
            mention = GradeCalculator.MentionFor(generalAverage, scale);
        }

        return new GradeSummaryDto(
            request.StudentId, request.TermId, subjects, totalCoefficients, totalPoints, generalAverage, mention);
    }
}
