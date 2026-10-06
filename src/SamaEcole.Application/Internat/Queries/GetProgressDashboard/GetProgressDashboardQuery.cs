using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetProgressDashboard;

/// <summary>
/// GET /api/v1/internat/progress-dashboard — la vue de la Direction sur la mémorisation de l'établissement : progression
/// globale, répartition des élèves par tranche d'avancement, synthèse par Halqa, et alertes de STAGNATION.
///
/// <b>Stagnation</b> = un élève rattaché à une Halqa, qui n'a pas terminé les 60 Hizb, et dont la dernière évaluation
/// remonte à plus de <see cref="StaleDays"/> jours — ou qui n'a jamais été évalué. C'est la seule alerte que le modèle
/// permet de poser honnêtement : « en retard » supposerait un rythme attendu que personne n'a fixé, et une
/// courbe d'évolution supposerait un historique que le suivi (un état courant par Hizb) ne conserve pas.
///
/// Ne porte que sur les élèves RATTACHÉS à un Oustaz (<c>Student.InstructorId</c>) : dans une école sans Halqa, le
/// tableau est vide plutôt que de noyer la Direction sous des élèves qui n'ont rien à voir avec le Coran.
/// </summary>
public record GetProgressDashboardQuery(int StaleDays = ProgressDashboardRules.DefaultStaleDays) : IRequest<ProgressDashboardDto>;

public static class ProgressDashboardRules
{
    public const int DefaultStaleDays = 30;
    public const int MinStaleDays = 1;
    public const int MaxStaleDays = 365;

    /// <summary>Plafond de la liste d'alertes renvoyée ; <see cref="ProgressDashboardDto.StagnantCount"/> garde le total réel.</summary>
    public const int MaxStagnantListed = 50;

    /// <summary>
    /// Tranches d'avancement, en % du Coran : « 0 » ne contient que les élèves à 0 % exactement ; les autres sont
    /// semi-ouvertes (Min, Max] — borne basse exclue, haute incluse (un élève à 10 % est dans « jusqu'à 10 % »).
    /// Toute valeur de 0 à 100 tombe dans une tranche et une seule.
    /// </summary>
    public static readonly (int Min, int Max)[] Bands = [(0, 0), (0, 10), (10, 25), (25, 50), (50, 75), (75, 100)];
}

/// <param name="Min">Borne basse de la tranche, en % du Coran (exclue ; la tranche 0-0 ne contient que les élèves à 0 %).</param>
/// <param name="Max">Borne haute de la tranche (incluse).</param>
public record ProgressBandDto(int Min, int Max, int StudentCount);

public record HalqaSummaryDto(
    Guid InstructorId,
    string InstructorName,
    string? InstructorNameAr,
    EntityStatus InstructorStatus,
    int StudentCount,
    decimal AverageProgressPercent,
    int CompletedHizbs,
    int StagnantCount);

/// <param name="LastEvaluatedAt">Dernière évaluation, ou <c>null</c> si l'élève n'a jamais été évalué.</param>
/// <param name="DaysSinceEvaluation">Jours écoulés depuis, ou <c>null</c> si jamais évalué.</param>
public record StagnantStudentDto(
    Guid StudentId,
    string Matricule,
    string FullName,
    string? FullNameAr,
    Guid InstructorId,
    string InstructorName,
    DateTimeOffset? LastEvaluatedAt,
    int? DaysSinceEvaluation,
    decimal ProgressPercent);

public record ProgressDashboardDto(
    DateTimeOffset GeneratedAt,
    int StaleDays,
    int StudentsInHalqa,
    int UnassignedStudents,
    decimal AverageProgressPercent,
    int CompletedHizbs,
    int CompletedQuarters,
    IReadOnlyList<ProgressBandDto> Bands,
    IReadOnlyList<HalqaSummaryDto> Halqas,
    int StagnantCount,
    IReadOnlyList<StagnantStudentDto> Stagnant);

public class GetProgressDashboardQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetProgressDashboardQuery, ProgressDashboardDto>
{
    public async Task<ProgressDashboardDto> Handle(GetProgressDashboardQuery request, CancellationToken cancellationToken)
    {
        var staleDays = Math.Clamp(request.StaleDays, ProgressDashboardRules.MinStaleDays, ProgressDashboardRules.MaxStaleDays);
        var now = timeProvider.GetUtcNow();
        var threshold = now.AddDays(-staleDays);

        // Global Query Filter + RLS bornent déjà toutes ces lectures à l'école courante.
        var unassigned = await dbContext.Students.CountAsync(s => s.InstructorId == null, cancellationToken);

        var students = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.InstructorId != null)
            .Select(s => new { s.Id, s.Matricule, s.FullName, s.FullNameAr, InstructorId = s.InstructorId!.Value })
            .ToListAsync(cancellationToken);

        var instructors = await dbContext.Instructors
            .AsNoTracking()
            .Select(i => new { i.Id, i.FullName, i.FullNameAr, i.Status })
            .ToListAsync(cancellationToken);

        // Une ligne par élève rattaché, calculée en base : jamais les 60 lignes de chaque élève en mémoire.
        var aggregates = (await dbContext.StudentHizbStatuses
                .AsNoTracking()
                .Where(h => dbContext.Students.Any(s => s.Id == h.StudentId && s.InstructorId != null))
                .GroupBy(h => h.StudentId)
                .Select(g => new
                {
                    StudentId = g.Key,
                    Completed = g.Count(h => h.State == HizbMemorizationState.Completed),
                    Quarters = g.Sum(h => h.CompletedQuarters),
                    LastEvaluatedAt = g.Max(h => h.LastEvaluatedAt)
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.StudentId);

        var instructorsById = instructors.ToDictionary(i => i.Id);

        var rows = students.Select(s =>
        {
            aggregates.TryGetValue(s.Id, out var a);
            var quarters = a?.Quarters ?? 0;
            var last = a?.LastEvaluatedAt;
            return new
            {
                Student = s,
                Completed = a?.Completed ?? 0,
                Quarters = quarters,
                Percent = HizbRules.ProgressPercent(quarters),
                LastEvaluatedAt = last,
                // Terminé = les 240 quarts : on ne relance pas un élève qui a fini le Coran.
                Finished = quarters >= HizbRules.TotalQuarters,
                Stale = quarters < HizbRules.TotalQuarters && (last is null || last < threshold)
            };
        }).ToList();

        var bands = ProgressDashboardRules.Bands
            .Select(b => new ProgressBandDto(b.Min, b.Max, rows.Count(r => InBand(r.Percent, b.Min, b.Max))))
            .ToList();

        var halqas = rows
            .GroupBy(r => r.Student.InstructorId)
            .Select(g =>
            {
                var instructor = instructorsById.GetValueOrDefault(g.Key);
                return new HalqaSummaryDto(
                    g.Key,
                    instructor?.FullName ?? string.Empty,
                    instructor?.FullNameAr,
                    instructor?.Status ?? EntityStatus.Active,
                    g.Count(),
                    Round1(g.Average(r => r.Percent)),
                    g.Sum(r => r.Completed),
                    g.Count(r => r.Stale));
            })
            .OrderBy(h => h.InstructorName)
            .ToList();

        // Jamais évalués d'abord, puis du plus ancien au plus récent : la relance la plus urgente en tête.
        var stale = rows
            .Where(r => r.Stale)
            .OrderBy(r => r.LastEvaluatedAt is null ? 0 : 1)
            .ThenBy(r => r.LastEvaluatedAt)
            .ThenBy(r => r.Student.FullName)
            .ToList();

        var stagnant = stale
            .Take(ProgressDashboardRules.MaxStagnantListed)
            .Select(r => new StagnantStudentDto(
                r.Student.Id, r.Student.Matricule, r.Student.FullName, r.Student.FullNameAr,
                r.Student.InstructorId,
                instructorsById.GetValueOrDefault(r.Student.InstructorId)?.FullName ?? string.Empty,
                r.LastEvaluatedAt,
                r.LastEvaluatedAt is { } last ? (int)Math.Floor((now - last).TotalDays) : null,
                r.Percent))
            .ToList();

        return new ProgressDashboardDto(
            now, staleDays, rows.Count, unassigned,
            rows.Count == 0 ? 0m : Round1(rows.Average(r => r.Percent)),
            rows.Sum(r => r.Completed),
            rows.Sum(r => r.Quarters),
            bands, halqas, stale.Count, stagnant);
    }

    private static bool InBand(decimal percent, int min, int max) =>
        min == max ? percent == min : percent > min && percent <= max;

    private static decimal Round1(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
