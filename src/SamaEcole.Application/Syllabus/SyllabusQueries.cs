using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Institutional;

namespace SamaEcole.Application.Syllabus;

public record SyllabusUnitDto(Guid Id, Guid SubjectId, string GradeLevel, string? Section, string Title, int Order, decimal? PlannedHours, uint RowVersion);

/// <summary>
/// GET /api/v1/syllabus/units?subjectId=&amp;gradeLevel= | ?subjectId=&amp;classroomId= — le programme d'une matière pour un
/// niveau, dans l'ordre du programme. Avec une classe, le niveau est celui de la classe (saisie du cahier de texte) ;
/// liste vide si son nom ne dit pas le niveau.
/// </summary>
public record GetSyllabusUnitsQuery(Guid SubjectId, string? GradeLevel, Guid? ClassroomId) : IRequest<SyllabusUnitsDto>;

/// <param name="HasTemplate">Vrai si une trame nationale existe pour cette matière et ce niveau (« Importer la trame »).</param>
public record SyllabusUnitsDto(string? GradeLevel, bool HasTemplate, IReadOnlyList<SyllabusUnitDto> Units);

public class GetSyllabusUnitsQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetSyllabusUnitsQuery, SyllabusUnitsDto>
{
    public async Task<SyllabusUnitsDto> Handle(GetSyllabusUnitsQuery request, CancellationToken cancellationToken)
    {
        var grade = request.GradeLevel;
        if (request.ClassroomId is { } classroomId)
        {
            var classroom = await dbContext.Classrooms.AsNoTracking()
                .Where(c => c.Id == classroomId).Select(c => new { c.Name, c.Cycle })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Classe {classroomId} introuvable dans votre établissement.");
            grade = AgeRules.GradeOf(classroom.Name, classroom.Cycle);
        }

        var subjectName = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == request.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Matière {request.SubjectId} introuvable dans votre établissement.");

        if (grade is null)
        {
            return new SyllabusUnitsDto(null, false, []);
        }

        var units = await dbContext.SyllabusUnits.AsNoTracking()
            .Where(u => u.SubjectId == request.SubjectId && u.GradeLevel == grade)
            .OrderBy(u => u.Order).ThenBy(u => u.Title)
            .Select(u => new SyllabusUnitDto(u.Id, u.SubjectId, u.GradeLevel, u.Section, u.Title, u.Order, u.PlannedHours,
                EF.Property<uint>(u, "xmin")))
            .ToListAsync(cancellationToken);

        return new SyllabusUnitsDto(grade, SyllabusTemplates.For(subjectName, grade) is not null, units);
    }
}

/// <summary>
/// GET /api/v1/syllabus/coverage — tableau de bord Directeur (Évolution N°7) : pour chaque classe et chaque matière dont
/// le programme est saisi pour le niveau de la classe, la part des unités pointées au cahier de texte de l'année active,
/// les enseignants de la classe dans cette matière (créneaux de l'emploi du temps, plus les auteurs du cahier), et la
/// dernière séance. Plus la moyenne par matière et niveau, et par enseignant : un enseignant qui ne tient jamais le
/// cahier apparaît à 0 %, c'est précisément ce que le Directeur doit voir.
/// </summary>
public record GetSyllabusCoverageQuery : IRequest<SyllabusCoverageDto>;

public record SyllabusCoverageRow(
    Guid ClassroomId, string ClassroomName, string GradeLevel, Guid SubjectId, string SubjectName,
    IReadOnlyList<string> Teachers, int CoveredUnits, int TotalUnits, decimal? Percent, DateOnly? LastSessionDate);

public record SyllabusSubjectSummary(string SubjectName, string GradeLevel, int Classes, decimal? AveragePercent);

public record SyllabusTeacherSummary(string TeacherName, int Classes, decimal? AveragePercent);

public record SyllabusCoverageDto(
    string? SchoolYearLabel, IReadOnlyList<SyllabusCoverageRow> Rows, IReadOnlyList<SyllabusSubjectSummary> BySubject,
    IReadOnlyList<SyllabusTeacherSummary> ByTeacher);

public class GetSyllabusCoverageQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetSyllabusCoverageQuery, SyllabusCoverageDto>
{
    public async Task<SyllabusCoverageDto> Handle(GetSyllabusCoverageQuery request, CancellationToken cancellationToken)
    {
        var year = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive).Select(y => new { y.Label, y.StartDate, y.EndDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (year is null)
        {
            return new SyllabusCoverageDto(null, [], [], []);
        }

        var units = await dbContext.SyllabusUnits.AsNoTracking()
            .Select(u => new { u.Id, u.SubjectId, u.GradeLevel })
            .ToListAsync(cancellationToken);
        var programmes = units.ToLookup(u => (u.SubjectId, u.GradeLevel), u => u.Id);

        var classrooms = (await dbContext.Classrooms.AsNoTracking().Select(c => new { c.Id, c.Name, c.Cycle }).ToListAsync(cancellationToken))
            .Select(c => new { c.Id, c.Name, Grade = AgeRules.GradeOf(c.Name, c.Cycle) })
            .Where(c => c.Grade is not null)
            .ToList();

        var subjects = await dbContext.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var entries = await (
            from e in dbContext.ClassJournalEntries.AsNoTracking()
            join t in dbContext.Teachers.AsNoTracking() on e.TeacherId equals t.Id
            where e.SessionDate >= year.StartDate && e.SessionDate <= year.EndDate
            select new { e.Id, e.ClassroomId, e.SubjectId, Teacher = t.FullName, e.SessionDate })
            .ToListAsync(cancellationToken);

        var scheduled = (await (
                from slot in dbContext.ScheduleSlots.AsNoTracking()
                join t in dbContext.Teachers.AsNoTracking() on slot.TeacherId equals t.Id
                select new { slot.ClassroomId, slot.SubjectId, Teacher = t.FullName })
            .Distinct().ToListAsync(cancellationToken))
            .ToLookup(s => (s.ClassroomId, s.SubjectId), s => s.Teacher);

        var entryIds = entries.Select(e => e.Id).ToList();
        var links = await dbContext.ClassJournalEntryUnits.AsNoTracking()
            .Where(l => entryIds.Contains(l.ClassJournalEntryId))
            .Select(l => new { l.ClassJournalEntryId, l.SyllabusUnitId })
            .ToListAsync(cancellationToken);
        var linksByEntry = links.ToLookup(l => l.ClassJournalEntryId, l => l.SyllabusUnitId);

        var rows = new List<SyllabusCoverageRow>();
        foreach (var classroom in classrooms)
        {
            foreach (var subjectId in programmes.Where(p => p.Key.GradeLevel == classroom.Grade).Select(p => p.Key.SubjectId).Distinct())
            {
                var programme = programmes[(subjectId, classroom.Grade!)].ToHashSet();
                var sessions = entries.Where(e => e.ClassroomId == classroom.Id && e.SubjectId == subjectId).ToList();
                var covered = sessions.SelectMany(s => linksByEntry[s.Id]).Where(programme.Contains).Distinct().Count();

                rows.Add(new SyllabusCoverageRow(
                    classroom.Id, classroom.Name, classroom.Grade!, subjectId, subjects.GetValueOrDefault(subjectId, "—"),
                    scheduled[(classroom.Id, subjectId)].Concat(sessions.Select(s => s.Teacher))
                        .Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList(),
                    covered, programme.Count,
                    SyllabusCoverage.Percent(programme, sessions.SelectMany(s => linksByEntry[s.Id])),
                    sessions.Count == 0 ? null : sessions.Max(s => s.SessionDate)));
            }
        }

        rows = rows.OrderBy(r => r.ClassroomName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.SubjectName, StringComparer.CurrentCultureIgnoreCase).ToList();

        var bySubject = rows
            .GroupBy(r => new { r.SubjectName, r.GradeLevel })
            .Select(g => new SyllabusSubjectSummary(g.Key.SubjectName, g.Key.GradeLevel, g.Count(),
                g.All(r => r.Percent is null) ? null : Math.Round(g.Average(r => r.Percent ?? 0m), 1)))
            .OrderBy(s => s.SubjectName, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.GradeLevel)
            .ToList();

        var byTeacher = rows
            .SelectMany(r => r.Teachers.Select(t => new { Teacher = t, r.Percent }))
            .GroupBy(x => x.Teacher)
            .Select(g => new SyllabusTeacherSummary(g.Key, g.Count(),
                g.All(x => x.Percent is null) ? null : Math.Round(g.Average(x => x.Percent ?? 0m), 1)))
            .OrderBy(t => t.TeacherName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new SyllabusCoverageDto(year.Label, rows, bySubject, byTeacher);
    }
}
