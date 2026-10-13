using SamaEcole.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetProgressHistory;

/// <summary>
/// GET /api/v1/internat/progress-history?from=&amp;to=&amp;instructorId= — la progression MOYENNE par mois des élèves en Halqa, pour toute
/// l'école ou pour une Halqa. Pour chaque mois : la moyenne, sur les élèves du périmètre, du total de quarts à la fin du mois (sur
/// 240) — un élève jamais évalué compte 0.
///
/// Périmètre = les élèves rattachés AUJOURD'HUI à un Oustaz (à celui demandé, le cas échéant) : un élève qui change de Halqa suit sa
/// courbe dans la nouvelle. Un Oustaz ne peut demander que SA Halqa (<see cref="HalqaScopeAuthorizer"/>). Par défaut : les six
/// derniers mois ; au plus <see cref="MaxMonths"/>. Seuls les mois à partir du premier enregistrement du journal sont renvoyés :
/// avant, il n'y a pas d'historique, et une moyenne à 0 % serait inventée.
/// </summary>
public record GetProgressHistoryQuery(DateOnly? From = null, DateOnly? To = null, Guid? InstructorId = null)
    : IRequest<ProgressHistoryDto>;

public class GetProgressHistoryQueryValidator : AbstractValidator<GetProgressHistoryQuery>
{
    public GetProgressHistoryQueryValidator()
    {
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From!.Value)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("La fin de période ne peut pas précéder son début.");
        RuleFor(q => q.InstructorId).NotEqual(Guid.Empty).When(q => q.InstructorId is not null);
    }
}

public class GetProgressHistoryQueryHandler(
    IApplicationDbContext dbContext, HalqaScopeAuthorizer scopeAuthorizer, TimeProvider timeProvider)
    : IRequestHandler<GetProgressHistoryQuery, ProgressHistoryDto>
{
    public const int MaxMonths = 24;
    public const int DefaultMonths = 6;

    public async Task<ProgressHistoryDto> Handle(GetProgressHistoryQuery request, CancellationToken cancellationToken)
    {
        // Un Oustaz ne lit que SA Halqa ; un rôle non borné peut viser une Halqa ou toute l'école.
        var own = await scopeAuthorizer.GetOwnInstructorIdOrNullAsync(cancellationToken);
        var instructorId = own ?? request.InstructorId;
        if (own is not null && request.InstructorId is { } asked && asked != own)
        {
            await scopeAuthorizer.EnsureCanAccessHalqaAsync(asked, cancellationToken);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var to = request.To ?? today;
        var from = request.From ?? new DateOnly(to.Year, to.Month, 1).AddMonths(-(DefaultMonths - 1));
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);
        if (((lastMonth.Year - firstMonth.Year) * 12) + lastMonth.Month - firstMonth.Month + 1 > MaxMonths)
        {
            firstMonth = lastMonth.AddMonths(-(MaxMonths - 1));
        }

        // Global Query Filter + RLS : seuls les élèves de l'école courante.
        var scope = dbContext.Students.AsNoTracking().Where(s => s.InstructorId != null);
        if (instructorId is { } id)
        {
            scope = scope.Where(s => s.InstructorId == id);
        }

        var studentIds = await scope.Select(s => s.Id).ToListAsync(cancellationToken);
        if (studentIds.Count == 0)
        {
            return new ProgressHistoryDto(instructorId, 0, null, []);
        }

        var rangeEnd = new DateTimeOffset(lastMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Données minimales (élève, date, total) : quelques dizaines de milliers de lignes au plus pour une grande école.
        var rows = await dbContext.StudentHizbEvaluations.AsNoTracking()
            .Where(e => studentIds.Contains(e.StudentId) && e.EvaluatedAt < rangeEnd)
            .OrderBy(e => e.StudentId).ThenBy(e => e.EvaluatedAt)
            .Select(e => new { e.StudentId, e.EvaluatedAt, e.TotalQuartersAfter })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new ProgressHistoryDto(instructorId, studentIds.Count, null, []);
        }

        var firstEvaluationAt = rows.Min(r => r.EvaluatedAt);
        var firstEvaluationMonth = new DateOnly(firstEvaluationAt.Year, firstEvaluationAt.Month, 1);
        var byStudent = rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        var months = new List<ProgressMonthDto>();
        for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
        {
            // Avant le premier enregistrement du journal : pas d'historique, donc pas de mois (jamais un 0 % inventé).
            if (month < firstEvaluationMonth)
            {
                continue;
            }

            var start = new DateTimeOffset(month.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(month.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var totalQuarters = 0L;
            var evaluationCount = 0;
            var studentsEvaluated = 0;
            foreach (var studentId in studentIds)
            {
                if (!byStudent.TryGetValue(studentId, out var list))
                {
                    continue;
                }

                // Dernière saisie ANTÉRIEURE à la fin du mois : le total de l'élève à ce moment-là.
                var last = list.LastOrDefault(r => r.EvaluatedAt < end);
                if (last is not null)
                {
                    totalQuarters += last.TotalQuartersAfter;
                }

                var inMonth = list.Count(r => r.EvaluatedAt >= start && r.EvaluatedAt < end);
                evaluationCount += inMonth;
                if (inMonth > 0)
                {
                    studentsEvaluated++;
                }
            }

            var average = Math.Round(
                totalQuarters * 100m / (HizbRules.TotalQuarters * studentIds.Count), 1, MidpointRounding.AwayFromZero);
            months.Add(new ProgressMonthDto(month, average, evaluationCount, studentsEvaluated));
        }

        return new ProgressHistoryDto(instructorId, studentIds.Count, firstEvaluationAt, months);
    }
}
