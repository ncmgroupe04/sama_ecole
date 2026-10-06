using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetStudentHizbProgress;

/// <summary>
/// GET /api/v1/internat/students/{studentId}/hizb-progress — la grille complète des 60 Hizb d'un élève. Les Hizb
/// jamais saisis sont synthétisés « non commencé » à la lecture : la grille a toujours 60 cases, dans l'ordre, sans
/// que la base ait à porter 60 lignes vides par élève.
/// </summary>
public record GetStudentHizbProgressQuery(Guid StudentId) : IRequest<StudentHizbProgressDto>;

public class GetStudentHizbProgressQueryHandler(IApplicationDbContext dbContext, HalqaScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<GetStudentHizbProgressQuery, StudentHizbProgressDto>
{
    public async Task<StudentHizbProgressDto> Handle(GetStudentHizbProgressQuery request, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : l'élève d'une autre école est structurellement introuvable (404).
        var student = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Id, s.FullName, s.InstructorId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.StudentId} introuvable.");

        await scopeAuthorizer.EnsureCanAccessStudentAsync(student.InstructorId, cancellationToken);

        var stored = (await dbContext.StudentHizbStatuses
                .AsNoTracking()
                .Where(h => h.StudentId == request.StudentId)
                .Select(h => new HizbCellDto(
                    h.HizbNumber, h.CompletedQuarters, h.State, h.LastEvaluatedAt, h.Rating,
                    EF.Property<uint>(h, "xmin")))
                .ToListAsync(cancellationToken))
            .ToDictionary(c => c.HizbNumber);

        var cells = Enumerable.Range(1, HizbRules.HizbCount)
            .Select(n => stored.TryGetValue(n, out var cell)
                ? cell
                : new HizbCellDto(n, 0, HizbMemorizationState.NotStarted, null, null, null))
            .ToList();

        var quarters = cells.Sum(c => c.CompletedQuarters);
        var summary = new HizbSummaryDto(
            cells.Count(c => c.State == HizbMemorizationState.Completed),
            cells.Count(c => c.State == HizbMemorizationState.InProgress),
            quarters,
            HizbRules.TotalQuarters,
            HizbRules.ProgressPercent(quarters));

        return new StudentHizbProgressDto(student.Id, student.FullName, student.InstructorId, summary, cells);
    }
}
