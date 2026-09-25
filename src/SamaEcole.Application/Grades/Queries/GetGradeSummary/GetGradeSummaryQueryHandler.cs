using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Queries.GetGradeSummary;

public class GetGradeSummaryQueryHandler(
    IApplicationDbContext dbContext, CoefficientOverrideLoader overrideLoader, SubjectFollowScope followScope)
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

        // L'année de la période : les surcharges de coefficient sont rattachées à l'année scolaire
        // (Évolution N°4, arbitrage A6), jamais à l'établissement en général.
        var schoolYearId = await dbContext.Terms.AsNoTracking()
            .Where(t => t.Id == request.TermId)
            .Select(t => (Guid?)t.SchoolYearId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Période {request.TermId} introuvable dans votre établissement.");

        // Cycle de la classe de l'élève : Maternelle & Primaire calculent une moyenne SIMPLE sur /10,
        // sans coefficients ni mention (ceux-ci n'appartiennent qu'au secondaire) ; Collège & Lycée
        // conservent la moyenne pondérée sur /20. Projection nullable : un élève sans classe → null →
        // traité comme secondaire.
        var cycle = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Join(dbContext.Classrooms.AsNoTracking(),
                s => s.ClassroomId, c => c.Id, (s, c) => (CycleType?)c.Cycle)
            .FirstOrDefaultAsync(cancellationToken);
        var isPrimaire = cycle is { } c && c.UsesSimplifiedGrading();

        // Barème du cycle : sert de repli aux matières sans MaxScore propre (toutes celles antérieures
        // aux grilles APC) ET de barème d'expression de la moyenne générale, vers lequel chaque ligne
        // est ramenée avant pondération.
        var cycleScale = GradingScaleGuard.ScaleForCycle(cycle);

        // Surcharges de coefficient (Évolution N°4) : classe, puis série, puis matière. Inutile de les
        // charger au primaire, qui neutralise le coefficient à 1 quoi qu'il arrive.
        var overrides = isPrimaire
            ? CoefficientOverrides.None
            : await overrideLoader.LoadAsync(request.StudentId, schoolYearId, cancellationToken);

        var rows = await (
            from g in dbContext.Grades.AsNoTracking()
            join s in dbContext.Subjects.AsNoTracking() on g.SubjectId equals s.Id
            where g.StudentId == request.StudentId && g.TermId == request.TermId
            select new
            {
                g.SubjectId,
                s.Name,
                s.Coefficient,
                s.MaxScore,
                s.ParentSubjectId,
                ParentName = dbContext.Subjects.AsNoTracking()
                    .Where(p => p.Id == s.ParentSubjectId)
                    .Select(p => p.Name)
                    .FirstOrDefault(),
                g.EvaluationType,
                g.Value
            }).ToListAsync(cancellationToken);

        // Matières que l'élève ne suit pas (Évolution N°6) : option d'un groupe qu'il n'a pas choisie, matière
        // désactivée pour sa classe. Elles sortent du bulletin — ni ligne vide, ni coefficient au total : le total
        // des coefficients est celui des matières EFFECTIVEMENT suivies. Classe non configurée : rien n'est exclu.
        var excluded = await followScope.ExcludedSubjectsAsync(request.StudentId, schoolYearId, cancellationToken);
        if (excluded.Count > 0)
        {
            rows = rows.Where(r => !excluded.Contains(r.SubjectId)).ToList();
        }

        // Moyenne d'une matière : sur ce qui a été saisi (Devoir seul, Composition seule, ou les deux) —
        // la saisie progresse au fil du trimestre, exiger les deux figerait l'écran tant qu'il manque
        // une note (Volume 1 §8.3 : « total des coefficients, total des points, moyenne générale »).
        // Formule PARTAGÉE avec GetStudentDetailQueryHandler (JGK-D02) via GradeCalculator — une seule
        // source de vérité pour la moyenne d'une matière et la moyenne générale pondérée.
        var subjects = rows
            .GroupBy(r => new { r.SubjectId, r.Name, r.Coefficient, r.MaxScore, r.ParentSubjectId, r.ParentName })
            .Select(g =>
            {
                var devoir1 = g.Where(r => r.EvaluationType == EvaluationType.Devoir1).Select(r => (decimal?)r.Value).FirstOrDefault();
                var devoir2 = g.Where(r => r.EvaluationType == EvaluationType.Devoir2).Select(r => (decimal?)r.Value).FirstOrDefault();
                var composition = g.Where(r => r.EvaluationType == EvaluationType.Composition).Select(r => (decimal?)r.Value).FirstOrDefault();
                var devoirAverage = GradeCalculator.DevoirAverage(devoir1, devoir2);

                // Toujours non-null : le groupe vient d'au moins une ligne de note (Devoir1, Devoir2 ou Composition).
                var average = GradeCalculator.SubjectAverage(devoir1, devoir2, composition)!.Value;

                // Primaire : coefficient neutralisé à 1 → la moyenne générale devient une moyenne simple
                // des matières. Le coefficient réel de la matière est volontairement ignoré (le primaire
                // n'a pas de système de coefficients). Secondaire : le coefficient EFFECTIF s'applique —
                // surcharge de classe, sinon de série, sinon celui de la matière (SubjectCoefficients).
                var coefficient = isPrimaire ? 1m : overrides.Effective(g.Key.SubjectId, g.Key.Coefficient);

                // Barème PROPRE à la ligne (grilles APC : /40, /60, /24, /16…), à défaut celui du cycle.
                var maxScore = GradeCalculator.EffectiveMaxScore(g.Key.MaxScore, cycleScale);

                // La moyenne reste BRUTE, sur le barème de la ligne : c'est la note saisie, celle
                // qu'imprime la colonne « Notes » en face de son « Sur ». Seuls les POINTS entrant dans
                // la moyenne générale sont ramenés au barème du bulletin — deux lignes /60 et /20 y
                // pèsent alors leur coefficient, et rien de plus.
                var weightedPoints = GradeCalculator.Rebase(average, maxScore, cycleScale) * coefficient;

                return new SubjectGradeDto(
                    g.Key.SubjectId,
                    g.Key.Name,
                    devoir1,
                    devoir2,
                    composition,
                    devoirAverage,
                    average,
                    coefficient,
                    weightedPoints,
                    maxScore,
                    g.Key.ParentSubjectId,
                    g.Key.ParentName);
            })
            .OrderBy(s => s.SubjectName)
            .ToList();

        // Sur la moyenne RAMENÉE au barème du cycle, jamais sur la moyenne brute : c'est la seule façon
        // qu'une grille APC mêlant /60, /40, /24 et /16 produise une moyenne générale lisible sur le
        // barème du bulletin. Quand aucune matière ne fixe son propre barème — le cas de toutes les
        // données existantes — la transposition est l'identité et le résultat est celui d'avant.
        var (totalCoefficients, totalPoints, generalAverageOrNull) =
            GradeCalculator.WeightedGeneralAverage(subjects.Select(s =>
                ((decimal?)GradeCalculator.Rebase(s.Average, s.MaxScore, cycleScale), s.Coefficient)));
        var generalAverage = generalAverageOrNull ?? 0m;

        // Aucune matière notée : rien à qualifier, jamais une mention par défaut trompeuse. Le Primaire
        // n'a pas de système de mentions (réservé au secondaire) : la mention y reste toujours nulle.
        string? mention = null;
        if (!isPrimaire && totalCoefficients > 0)
        {
            var scale = await MentionScale.ResolveAsync(dbContext, cancellationToken);
            mention = GradeCalculator.MentionFor(generalAverage, scale);
        }

        // Matières obligatoires dispensées : déjà retirées du calcul par SubjectFollowScope ; on les remonte pour que le
        // bulletin les marque. Coefficient effectif comme pour les lignes notées ; neutralisé à 1 au primaire.
        var exemptSubjects = (await followScope.ExemptionsAsync(request.StudentId, schoolYearId, cancellationToken))
            .Select(e => new ExemptSubjectDto(
                e.SubjectId, e.Name, isPrimaire ? 1m : overrides.Effective(e.SubjectId, e.Coefficient)))
            .OrderBy(e => e.SubjectName)
            .ToList();

        return new GradeSummaryDto(
            request.StudentId, request.TermId, subjects, totalCoefficients, totalPoints, generalAverage, mention,
            exemptSubjects);
    }
}
