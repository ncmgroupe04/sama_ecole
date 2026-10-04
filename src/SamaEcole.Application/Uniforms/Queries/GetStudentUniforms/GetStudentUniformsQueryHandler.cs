using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Queries.GetStudentUniforms;

public class GetStudentUniformsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentUniformsQuery, StudentUniformsResult>
{
    public async Task<StudentUniformsResult> Handle(GetStudentUniformsQuery request, CancellationToken cancellationToken)
    {
        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.StudentId} introuvable.");

        var enrollments = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.StudentId == student.Id && e.Status != EnrollmentStatus.Cancelled)
            .Join(dbContext.SchoolYears.AsNoTracking(), e => e.SchoolYearId, y => y.Id, (e, y) => new { e, y })
            .Join(dbContext.Classrooms.AsNoTracking(), x => x.e.ClassroomId, c => c.Id,
                (x, c) => new
                {
                    x.e.Id, x.e.SchoolYearId, x.e.TotalDue, x.e.AmountPaid,
                    YearLabel = x.y.Label, YearStart = x.y.StartDate, YearActive = x.y.IsActive,
                    ClassroomName = c.Name, ClassroomCycle = c.Cycle
                })
            .ToListAsync(cancellationToken);

        var enrollmentIds = enrollments.Select(e => e.Id).ToList();

        var lines = await StudentUniformReader.ReadAsync(
            dbContext, dbContext.StudentUniforms.Where(u => enrollmentIds.Contains(u.EnrollmentId)), cancellationToken);

        var billedCategories = await StudentUniformReader.BilledCategoriesAsync(dbContext, enrollmentIds, cancellationToken);

        var catalogue = await dbContext.UniformItems.AsNoTracking().OrderBy(i => i.Name).ToListAsync(cancellationToken);

        var result = enrollments
            .OrderByDescending(e => e.YearStart)
            .Select(e =>
            {
                var enrollmentLines = lines.Where(l => l.EnrollmentId == e.Id).ToList();
                var billed = billedCategories.GetValueOrDefault(e.Id) ?? [];

                // Les lignes qui occupent encore la place « active » d'un modèle : on ne le repropose pas.
                var activeItemIds = enrollmentLines
                    .Where(l => l.Status is nameof(StudentUniformStatus.Requested) or nameof(StudentUniformStatus.Delivered))
                    .Select(l => l.UniformItemId)
                    .ToHashSet();

                // Proposé = la famille l'a PAYÉ (ligne de frais de la catégorie du modèle), il convient à l'élève, et
                // aucune ligne active n'existe encore. Un modèle sans frais n'est jamais proposé, mais reste saisissable.
                var proposals = catalogue
                    .Where(i => i.FeeCategoryId is { } fee && billed.Contains(fee)
                                && !activeItemIds.Contains(i.Id)
                                && UniformProposalRules.Matches(i.Gender, i.Cycle, student.Gender, e.ClassroomCycle))
                    .Select(i => new UniformProposalDto(i.Id, i.Name, i.Gender.ToString(), i.QuantityIncluded))
                    .ToList();

                var balance = Math.Max(0m, e.TotalDue - e.AmountPaid);

                return new StudentUniformEnrollmentDto(
                    e.Id, e.SchoolYearId, e.YearLabel, e.YearActive, e.ClassroomName,
                    e.TotalDue, e.AmountPaid, balance, balance > 0m, enrollmentLines, proposals);
            })
            .ToList();

        return new StudentUniformsResult(student.Id, student.FullName, result);
    }
}
