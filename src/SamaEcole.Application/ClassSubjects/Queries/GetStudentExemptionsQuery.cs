using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exemptions;

namespace SamaEcole.Application.ClassSubjects.Queries;

/// <summary>
/// Matières que l'on peut dispenser à un élève (obligatoires et autonomes dans sa classe), avec l'éventuelle dispense
/// de l'année ACTIVE et son motif, et le nombre de notes de l'année qu'une dispense masquerait. Le motif peut être
/// médical : la route est réservée au Directeur et au Secrétariat, et il n'est renvoyé par AUCUNE autre requête.
/// </summary>
public record GetStudentExemptionsQuery(Guid StudentId) : IRequest<StudentExemptionsDto>;

public record StudentExemptionsDto(Guid StudentId, Guid SchoolYearId, IReadOnlyList<ExemptibleSubjectDto> Subjects);

public record ExemptibleSubjectDto(Guid SubjectId, string Name, bool IsExempt, string? Reason, int GradeCount);

public class GetStudentExemptionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentExemptionsQuery, StudentExemptionsDto>
{
    public async Task<StudentExemptionsDto> Handle(GetStudentExemptionsQuery request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new KeyNotFoundException($"Élève {request.StudentId} introuvable dans votre établissement.");
        }

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);
        var classroomId = await StudentYearClassroom.ResolveAsync(dbContext, request.StudentId, yearId, cancellationToken);
        if (classroomId is null)
        {
            return new StudentExemptionsDto(request.StudentId, yearId, []);
        }

        var dispensable = await ExemptionQueries.DispensableAsync(dbContext, classroomId.Value, cancellationToken);

        var reasons = await dbContext.StudentSubjectExemptions.AsNoTracking()
            .Where(x => x.StudentId == request.StudentId && x.SchoolYearId == yearId)
            .Select(x => new { x.SubjectId, x.Reason })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Reason, cancellationToken);

        var subjectIds = dispensable.Select(s => s.SubjectId).Concat(reasons.Keys).Distinct().ToList();
        var gradeCounts = await (
            from g in dbContext.Grades.AsNoTracking()
            join t in dbContext.Terms.AsNoTracking() on g.TermId equals t.Id
            where g.StudentId == request.StudentId && t.SchoolYearId == yearId && subjectIds.Contains(g.SubjectId)
            group g by g.SubjectId into grouped
            select new { SubjectId = grouped.Key, Count = grouped.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count, cancellationToken);

        var subjects = dispensable
            .Select(s => reasons.TryGetValue(s.SubjectId, out var reason)
                ? new ExemptibleSubjectDto(s.SubjectId, s.Name, true, reason, gradeCounts.GetValueOrDefault(s.SubjectId))
                : new ExemptibleSubjectDto(s.SubjectId, s.Name, false, null, gradeCounts.GetValueOrDefault(s.SubjectId)))
            .ToList();

        return new StudentExemptionsDto(request.StudentId, yearId, subjects);
    }
}
