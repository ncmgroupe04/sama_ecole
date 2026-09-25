using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Institutional.Queries;

/// <summary>
/// GET /api/v1/institutional/ief-report?schoolYearId= — rapport de rentrée IEF (Évolution N°7). Une seule agrégation,
/// réutilisée TELLE QUELLE par l'écran, le PDF et l'Excel : jamais trois calculs qui divergent.
///
/// Élèves : les INSCRIPTIONS non annulées de l'année (comme STATEDUC), dans la classe de leur inscription. Âges
/// révolus à <see cref="AgeReferenceDate"/> (défaut : 31 décembre de l'année de rentrée). Enseignants : les actifs ;
/// disciplines et volume horaire lus sur l'emploi du temps courant (créneaux hebdomadaires).
/// </summary>
public record GetIefReportQuery(Guid SchoolYearId, DateOnly? AgeReferenceDate = null) : IRequest<IefReportDto>;

public class GetIefReportQueryHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, TimeProvider timeProvider)
    : IRequestHandler<GetIefReportQuery, IefReportDto>
{
    public async Task<IefReportDto> Handle(GetIefReportQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement dans le jeton d'authentification.");

        var school = await dbContext.Schools.AsNoTracking().FirstAsync(s => s.Id == schoolId, cancellationToken);
        var year = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException("Année scolaire introuvable dans votre établissement.");

        var referenceDate = request.AgeReferenceDate ?? AgeRules.ReferenceDate(year.StartDate);
        var norms = await AgeRules.ResolveAsync(dbContext, cancellationToken);

        var enrolled = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            where e.SchoolYearId == year.Id && e.Status != EnrollmentStatus.Cancelled
            select new
            {
                ClassroomId = c.Id, ClassroomName = c.Name, c.Level, c.Cycle,
                s.Gender, s.BirthDate, e.IsRepeating, e.IsTransferredIn
            }).ToListAsync(cancellationToken);

        var students = enrolled.Select(x =>
        {
            var grade = AgeRules.GradeOf(x.ClassroomName, x.Cycle);
            var age = AgeRules.AgeAt(x.BirthDate, referenceDate);
            var norm = grade is not null && norms.TryGetValue(grade, out var n) ? n : null;
            return new
            {
                x.ClassroomId, x.ClassroomName, x.Level, x.Cycle, Grade = grade, Norm = norm, Age = age,
                Girl = IsGirl(x.Gender), Boy = IsBoy(x.Gender),
                Status = StudentEntryStatuses.Of(x.IsRepeating, x.IsTransferredIn),
                AgeStatus = AgeRules.Classify(age, norm)
            };
        }).ToList();

        var columns = AgeBuckets.Build(students.Select(s => s.Age));

        var classes = students
            .GroupBy(s => new { s.ClassroomId, s.ClassroomName, s.Cycle, s.Grade, s.Norm })
            .OrderBy(g => g.Key.Cycle)
            .ThenBy(g => GradeOrder(g.Key.Grade))
            .ThenBy(g => g.Key.ClassroomName, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new IefClassRow(
                g.Key.ClassroomName,
                g.Key.Grade,
                g.Key.Cycle,
                g.Count(s => s.Girl),
                g.Count(s => s.Boy),
                g.Count(),
                g.Count(s => s.Status == StudentEntryStatus.New),
                g.Count(s => s.Status == StudentEntryStatus.Repeater),
                g.Count(s => s.Status == StudentEntryStatus.Transferred),
                g.Count(s => s.AgeStatus == AgeNormStatus.Early),
                g.Count(s => s.AgeStatus == AgeNormStatus.Late),
                g.Key.Norm is { } n ? $"{n.MinAge}–{n.MaxAge} ans" : null,
                columns.Select(b => new IefAgeCell(
                    g.Count(s => s.Girl && b.Contains(s.Age)),
                    g.Count(s => s.Boy && b.Contains(s.Age)))).ToList(),
                g.Count(s => s.Age is null),
                g.Count(s => !s.Girl && !s.Boy)))
            .ToList();

        var repetition = students
            .GroupBy(s => new { Level = s.Grade ?? s.Level, s.Cycle, Order = GradeOrder(s.Grade) })
            .OrderBy(g => g.Key.Cycle).ThenBy(g => g.Key.Order).ThenBy(g => g.Key.Level)
            .Select(g => new IefRepetitionRow(
                g.Key.Level,
                g.Count(),
                g.Count(s => s.Status == StudentEntryStatus.Repeater),
                g.Count(s => s.Status == StudentEntryStatus.Repeater && s.Girl),
                g.Count(s => s.Status == StudentEntryStatus.Repeater && s.Boy)))
            .ToList();

        var (byDiscipline, byDiploma, teacherRows) = await BuildTeachersAsync(cancellationToken);

        return new IefReportDto(
            school.Name, school.NationalSchoolCode, school.InspectionAcademie, school.InspectionEducationFormation,
            year.Label, referenceDate, timeProvider.GetUtcNow(), columns, classes, repetition,
            byDiscipline, byDiploma, teacherRows);
    }

    private async Task<(List<IefDisciplineRow>, List<IefDiplomaRow>, List<IefTeacherRow>)> BuildTeachersAsync(
        CancellationToken cancellationToken)
    {
        var teachers = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.Status == EntityStatus.Active)
            .Select(t => new { t.Id, t.FullName, t.Gender, t.AcademicQualification, t.ProfessionalQualification })
            .ToListAsync(cancellationToken);

        var slots = await (
            from slot in dbContext.ScheduleSlots.AsNoTracking()
            join subject in dbContext.Subjects.AsNoTracking() on slot.SubjectId equals subject.Id
            select new { slot.TeacherId, Subject = subject.Name, slot.StartTime, slot.EndTime })
            .ToListAsync(cancellationToken);

        var qualified = await (
            from ts in dbContext.TeacherSubjects.AsNoTracking()
            join subject in dbContext.Subjects.AsNoTracking() on ts.SubjectId equals subject.Id
            select new { ts.TeacherId, Subject = subject.Name })
            .ToListAsync(cancellationToken);

        static decimal Hours(TimeOnly start, TimeOnly end) =>
            end > start ? Math.Round((decimal)(end - start).TotalHours, 2) : 0m;

        var rows = teachers.Select(t =>
        {
            var own = slots.Where(s => s.TeacherId == t.Id).ToList();
            var disciplines = (own.Count > 0
                    ? own.Select(s => s.Subject)
                    : qualified.Where(q => q.TeacherId == t.Id).Select(q => q.Subject))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new IefTeacherRow(t.FullName, t.Gender, disciplines, t.AcademicQualification, t.ProfessionalQualification,
                own.Sum(s => Hours(s.StartTime, s.EndTime)));
        }).OrderBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

        var activeIds = teachers.Select(t => t.Id).ToHashSet();
        var byDiscipline = rows
            .SelectMany(r => (r.Disciplines.Count > 0 ? r.Disciplines : ["Non renseignée"]).Select(d => (Discipline: d, Teacher: r)))
            .GroupBy(x => x.Discipline, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new IefDisciplineRow(
                g.Key,
                g.Count(),
                g.Count(x => IsBoy(x.Teacher.Gender)),
                g.Count(x => IsGirl(x.Teacher.Gender)),
                slots.Where(s => activeIds.Contains(s.TeacherId) && string.Equals(s.Subject, g.Key, StringComparison.CurrentCultureIgnoreCase))
                    .Sum(s => Hours(s.StartTime, s.EndTime))))
            .ToList();

        var byDiploma = rows
            .GroupBy(r => new { r.Academic, r.Professional })
            .OrderBy(g => g.Key.Professional).ThenBy(g => g.Key.Academic)
            .Select(g => new IefDiplomaRow(
                g.Key.Academic, g.Key.Professional,
                g.Count(r => IsBoy(r.Gender)), g.Count(r => IsGirl(r.Gender)),
                g.Count(r => !IsBoy(r.Gender) && !IsGirl(r.Gender))))
            .ToList();

        return (byDiscipline, byDiploma, rows);
    }

    private static int GradeOrder(string? grade)
    {
        for (var i = 0; i < AgeNormTemplates.NormalAges.Count; i++)
        {
            if (AgeNormTemplates.NormalAges[i].Grade == grade) return i;
        }

        return int.MaxValue;
    }

    // Sexe « F » / « M » (Student.Gender), « G » accepté pour garçon ; casse et espaces ignorés. Une valeur non
    // reconnue n'est ni fille ni garçon : l'écart se voit dans le tableau au lieu d'être masqué.
    private static bool IsGirl(string? gender) => gender?.Trim().Equals("F", StringComparison.OrdinalIgnoreCase) ?? false;

    private static bool IsBoy(string? gender) =>
        gender?.Trim() is { } g && (g.Equals("M", StringComparison.OrdinalIgnoreCase) || g.Equals("G", StringComparison.OrdinalIgnoreCase));
}
