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
        var subjects = rows
            .GroupBy(r => new { r.SubjectId, r.Name, r.Coefficient })
            .Select(g =>
            {
                var average = g.Average(r => r.Value);
                return new SubjectGradeDto(
                    g.Key.SubjectId,
                    g.Key.Name,
                    g.Where(r => r.EvaluationType == EvaluationType.Devoir).Select(r => (decimal?)r.Value).FirstOrDefault(),
                    g.Where(r => r.EvaluationType == EvaluationType.Composition).Select(r => (decimal?)r.Value).FirstOrDefault(),
                    average,
                    g.Key.Coefficient,
                    average * g.Key.Coefficient);
            })
            .OrderBy(s => s.SubjectName)
            .ToList();

        var totalCoefficients = subjects.Sum(s => s.Coefficient);
        var totalPoints = subjects.Sum(s => s.WeightedPoints);
        var generalAverage = totalCoefficients > 0 ? totalPoints / totalCoefficients : 0m;

        // Aucune matière notée : rien à qualifier, jamais une mention par défaut trompeuse.
        string? mention = null;
        if (totalCoefficients > 0)
        {
            var scale = await MentionScale.ResolveAsync(dbContext, cancellationToken);
            mention = scale.FirstOrDefault(m => generalAverage >= m.MinAverage).Label;
        }

        return new GradeSummaryDto(
            request.StudentId, request.TermId, subjects, totalCoefficients, totalPoints, generalAverage, mention);
    }
}
