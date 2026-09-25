using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.OptionalSubjects;

namespace SamaEcole.Application.Enrollments.Queries.GetEnrollmentOptions;

/// <summary>
/// GET /api/v1/enrollments/{id}/options — les matières optionnelles du niveau de la classe courante de
/// l'élève, groupées, avec l'état de chacune (suivie / dispensée), ET les matières obligatoires du niveau
/// avec leur éventuelle dispense et son motif, plus le nombre de notes de l'année qu'une dispense masquerait.
/// <see cref="EnrollmentOptionsDto.HasExplicitChoice"/> est faux tant qu'AUCUNE option n'a de dispense :
/// l'élève suit alors TOUTES les options (comportement d'avant), même s'il est dispensé d'une matière obligatoire.
/// Le motif d'une dispense (potentiellement médical) n'est renvoyé que par cette requête.
/// </summary>
public record GetEnrollmentOptionsQuery(Guid EnrollmentId) : IRequest<EnrollmentOptionsDto>;

public record EnrollmentOptionsDto(
    Guid EnrollmentId,
    bool HasExplicitChoice,
    IReadOnlyList<OptionGroupDto> Groups,
    IReadOnlyList<MandatorySubjectDto>? MandatorySubjects = null);

/// <summary><see cref="Group"/> est null pour une option sans groupe (cumulable) : une entrée par matière.</summary>
public record OptionGroupDto(string? Group, IReadOnlyList<OptionSubjectDto> Subjects);

public record OptionSubjectDto(Guid SubjectId, string Name, bool IsFollowed, int GradeCount);

/// <summary>Une matière obligatoire du niveau : dispensée ou non, et son motif si oui.</summary>
public record MandatorySubjectDto(Guid SubjectId, string Name, bool IsExempt, string? Reason, int GradeCount);

public class GetEnrollmentOptionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEnrollmentOptionsQuery, EnrollmentOptionsDto>
{
    public async Task<EnrollmentOptionsDto> Handle(GetEnrollmentOptionsQuery request, CancellationToken cancellationToken)
    {
        var enrollment = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Id == request.EnrollmentId)
            .Select(e => new { e.Id, e.StudentId, e.SchoolYearId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        var level = await (from s in dbContext.Students.AsNoTracking()
                           join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
                           where s.Id == enrollment.StudentId
                           select c.Level).FirstOrDefaultAsync(cancellationToken)
                    ?? string.Empty;

        var options = await EnrollmentOptionsPlanner.LoadLevelOptionsAsync(dbContext, level, cancellationToken);
        var mandatory = await EnrollmentOptionsPlanner.LoadLevelMandatoryAsync(dbContext, level, cancellationToken);

        var rows = (await dbContext.EnrollmentSubjectExemptions.AsNoTracking()
                .Where(x => x.EnrollmentId == enrollment.Id)
                .Select(x => new { x.SubjectId, x.Reason })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.SubjectId, x => x.Reason);

        var subjectIds = options.Select(o => o.Id).Concat(mandatory.Select(m => m.Id)).ToList();
        var gradeCounts = await (
            from g in dbContext.Grades.AsNoTracking()
            join t in dbContext.Terms.AsNoTracking() on g.TermId equals t.Id
            where g.StudentId == enrollment.StudentId
                  && t.SchoolYearId == enrollment.SchoolYearId
                  && subjectIds.Contains(g.SubjectId)
            group g by g.SubjectId into grouped
            select new { SubjectId = grouped.Key, Count = grouped.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count, cancellationToken);

        OptionSubjectDto ToDto(LevelSubject o) =>
            new(o.Id, o.Name, !rows.ContainsKey(o.Id), gradeCounts.GetValueOrDefault(o.Id));

        // Groupes d'abord (déjà triés par LoadLevelOptionsAsync), puis les options sans groupe, une par entrée.
        var groups = options
            .Where(o => o.Group is not null)
            .GroupBy(o => o.Group!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new OptionGroupDto(g.Key, g.Select(ToDto).ToList()))
            .Concat(options.Where(o => o.Group is null)
                .Select(o => new OptionGroupDto(null, [ToDto(o)])))
            .ToList();

        var mandatorySubjects = mandatory
            .Select(m =>
            {
                var exempt = rows.TryGetValue(m.Id, out var reason) && reason is not null;
                return new MandatorySubjectDto(m.Id, m.Name, exempt, exempt ? reason : null, gradeCounts.GetValueOrDefault(m.Id));
            })
            .ToList();

        // « Choix explicite » : au moins UNE option a une dispense. Une dispense d'EPS seule n'est pas un choix d'options.
        var hasExplicitChoice = options.Any(o => rows.ContainsKey(o.Id));

        return new EnrollmentOptionsDto(enrollment.Id, hasExplicitChoice, groups, mandatorySubjects);
    }
}
