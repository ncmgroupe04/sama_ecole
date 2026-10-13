using SamaEcole.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetStudentHizbHistory;

/// <summary>
/// GET /api/v1/internat/students/{studentId}/hizb-history?from=&amp;to= — l'historique des évaluations Hizb d'un élève : les points de
/// sa courbe (total de quarts à la fin de chaque jour où il a été évalué) et ses dernières évaluations.
///
/// Même portée que la grille (<see cref="HalqaScopeAuthorizer"/>) : un Oustaz ne lit que SA Halqa, 404 pour un élève d'une autre
/// école. Le journal commence au déploiement : <see cref="StudentHizbHistoryDto.FirstEvaluationAt"/> dit à l'écran d'où il part.
/// </summary>
public record GetStudentHizbHistoryQuery(Guid StudentId, DateOnly? From = null, DateOnly? To = null)
    : IRequest<StudentHizbHistoryDto>;

public class GetStudentHizbHistoryQueryValidator : AbstractValidator<GetStudentHizbHistoryQuery>
{
    public GetStudentHizbHistoryQueryValidator()
    {
        RuleFor(q => q.StudentId).NotEmpty();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From!.Value)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("La fin de période ne peut pas précéder son début.");
    }
}

public class GetStudentHizbHistoryQueryHandler(IApplicationDbContext dbContext, HalqaScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<GetStudentHizbHistoryQuery, StudentHizbHistoryDto>
{
    /// <summary>Dernières évaluations renvoyées : assez pour une liste d'écran ou de bulletin, jamais tout le journal.</summary>
    public const int RecentCount = 20;

    public async Task<StudentHizbHistoryDto> Handle(GetStudentHizbHistoryQuery request, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : l'élève d'une autre école est structurellement introuvable (404).
        var student = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Id, s.FullName, s.InstructorId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.StudentId} introuvable.");

        await scopeAuthorizer.EnsureCanAccessStudentAsync(student.InstructorId, cancellationToken);

        var fromAt = request.From is { } f ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : (DateTimeOffset?)null;
        // Borne haute EXCLUSIVE : le lendemain à 00:00 UTC, pour inclure toute la journée `To`.
        var toExclusive = request.To is { } t ? new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : (DateTimeOffset?)null;

        var journal = dbContext.StudentHizbEvaluations.AsNoTracking().Where(e => e.StudentId == request.StudentId);

        var firstEvaluationAt = await journal.OrderBy(e => e.EvaluatedAt).Select(e => (DateTimeOffset?)e.EvaluatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Total au DÉBUT de la période : la dernière saisie antérieure. Sans borne basse, la courbe part du début du journal.
        int? opening = null;
        if (fromAt is { } start)
        {
            opening = await journal.Where(e => e.EvaluatedAt < start)
                .OrderByDescending(e => e.EvaluatedAt)
                .Select(e => (int?)e.TotalQuartersAfter)
                .FirstOrDefaultAsync(cancellationToken);
            // Rien avant la période : si le journal a commencé avant, l'élève était à 0 ; sinon pas d'historique (null).
            if (opening is null && firstEvaluationAt is { } first && first < start)
            {
                opening = 0;
            }
        }

        var inPeriod = journal;
        if (fromAt is { } a) inPeriod = inPeriod.Where(e => e.EvaluatedAt >= a);
        if (toExclusive is { } b) inPeriod = inPeriod.Where(e => e.EvaluatedAt < b);

        var rows = await inPeriod.OrderBy(e => e.EvaluatedAt)
            .Select(e => new { e.EvaluatedAt, e.TotalQuartersAfter })
            .ToListAsync(cancellationToken);

        // Un point par JOUR : la dernière saisie de la journée fait foi (plusieurs Hizb peuvent être saisis le même jour).
        var points = rows
            .GroupBy(r => DateOnly.FromDateTime(r.EvaluatedAt.UtcDateTime))
            .Select(g => g.Last())
            .Select(r => new HizbHistoryPointDto(
                DateOnly.FromDateTime(r.EvaluatedAt.UtcDateTime), r.TotalQuartersAfter, HizbRules.ProgressPercent(r.TotalQuartersAfter)))
            .ToList();

        var recent = await inPeriod.OrderByDescending(e => e.EvaluatedAt).Take(RecentCount)
            .Select(e => new
            {
                e.EvaluatedAt, e.HizbNumber, e.QuartersBefore, e.QuartersAfter, e.Rating, e.TotalQuartersAfter, e.IsBaseline,
                e.EvaluatedByUserId
            })
            .ToListAsync(cancellationToken);

        var userIds = recent.Where(r => r.EvaluatedByUserId != null).Select(r => r.EvaluatedByUserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var entries = recent
            .Select(r => new HizbEvaluationEntryDto(
                r.EvaluatedAt, r.HizbNumber, r.QuartersBefore, r.QuartersAfter, r.Rating, r.TotalQuartersAfter, r.IsBaseline,
                r.EvaluatedByUserId is { } id && names.TryGetValue(id, out var name) ? name : null))
            .ToList();

        var currentTotal = await dbContext.StudentHizbStatuses.AsNoTracking()
            .Where(h => h.StudentId == request.StudentId)
            .SumAsync(h => (int?)h.CompletedQuarters, cancellationToken) ?? 0;

        return new StudentHizbHistoryDto(
            student.Id, student.FullName, request.From, request.To, currentTotal, HizbRules.ProgressPercent(currentTotal),
            opening, firstEvaluationAt, points, entries);
    }
}
