using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetInstructorStudents;

/// <summary>
/// GET /api/v1/internat/instructors/{instructorId}/students — la Halqa d'un Oustaz : ses élèves, chacun avec son
/// indicateur global de mémorisation. Un Oustaz ne lit que SA Halqa (<see cref="HalqaScopeAuthorizer"/>) ; le
/// Directeur lit toutes les Halqa.
/// </summary>
public record GetInstructorStudentsQuery(Guid InstructorId) : IRequest<HalqaDto>;

public class GetInstructorStudentsQueryHandler(IApplicationDbContext dbContext, HalqaScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<GetInstructorStudentsQuery, HalqaDto>
{
    public async Task<HalqaDto> Handle(GetInstructorStudentsQuery request, CancellationToken cancellationToken)
    {
        // Portée AVANT toute lecture : un Oustaz qui vise la Halqa d'un autre reçoit 403, qu'elle existe ou non.
        await scopeAuthorizer.EnsureCanAccessHalqaAsync(request.InstructorId, cancellationToken);

        return await HalqaReader.LoadAsync(dbContext, request.InstructorId, cancellationToken);
    }
}

/// <summary>
/// Lecture d'une Halqa (Oustaz + élèves + indicateurs), partagée par GET instructors/{id}/students et GET my-halqa.
/// NE VÉRIFIE PAS la portée : l'appelant l'a déjà fait (HalqaScopeAuthorizer), c'est pourquoi la classe est interne.
/// </summary>
internal static class HalqaReader
{
    public static async Task<HalqaDto> LoadAsync(
        IApplicationDbContext dbContext, Guid instructorId, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : l'Oustaz d'une autre école est structurellement introuvable (404).
        var instructor = await dbContext.Instructors
            .AsNoTracking()
            .Where(i => i.Id == instructorId)
            .Select(i => new { i.Id, i.FullName, i.FullNameAr })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Oustaz {instructorId} introuvable.");

        var students = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.InstructorId == instructorId)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName, s.FullNameAr })
            .ToListAsync(cancellationToken);

        // Agrégat calculé en base, par élève de la Halqa : jamais les 60 lignes de chaque élève en mémoire.
        var aggregates = (await dbContext.StudentHizbStatuses
                .AsNoTracking()
                .Where(h => dbContext.Students.Any(s => s.Id == h.StudentId && s.InstructorId == instructorId))
                .GroupBy(h => h.StudentId)
                .Select(g => new
                {
                    StudentId = g.Key,
                    Completed = g.Count(h => h.State == HizbMemorizationState.Completed),
                    InProgress = g.Count(h => h.State == HizbMemorizationState.InProgress),
                    Quarters = g.Sum(h => h.CompletedQuarters),
                    LastEvaluatedAt = g.Max(h => h.LastEvaluatedAt)
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.StudentId);

        var rows = students
            .Select(s =>
            {
                aggregates.TryGetValue(s.Id, out var a);
                var quarters = a?.Quarters ?? 0;
                return new HalqaStudentDto(
                    s.Id, s.Matricule, s.FullName, s.FullNameAr,
                    a?.Completed ?? 0, a?.InProgress ?? 0, quarters,
                    HizbRules.ProgressPercent(quarters), a?.LastEvaluatedAt);
            })
            .ToList();

        var average = rows.Count == 0 ? 0m : Math.Round(rows.Average(r => r.ProgressPercent), 1, MidpointRounding.AwayFromZero);

        return new HalqaDto(
            instructor.Id, instructor.FullName, instructor.FullNameAr, rows.Count, average, rows);
    }
}
