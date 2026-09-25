using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Institutional;

namespace SamaEcole.Application.HourVolumes;

/// <param name="TemplateHours">Volume de la grille codée ; null si la grille n'a pas cette matière.</param>
/// <param name="GradeHours">Réglage de l'école pour le niveau, toutes séries (seulement quand une série est demandée).</param>
/// <param name="SchoolHours">Réglage de l'école pour CE niveau et CETTE série — la ligne que l'écran modifie.</param>
public record WeeklyHourNormRow(
    Guid? SubjectId, string SubjectName, string? OptionGroup, decimal? TemplateHours, decimal? GradeHours,
    decimal? SchoolHours, decimal? EffectiveHours, Guid? OverrideId, uint? RowVersion);

public record WeeklyHourNormsDto(string GradeLevel, string? Series, bool HasTemplate, decimal? TotalHours, IReadOnlyList<WeeklyHourNormRow> Rows);

/// <summary>
/// GET /api/v1/hour-volumes/norms?gradeLevel=&amp;series= — volumes horaires d'un niveau (et d'une série) : la grille
/// codée, les réglages de l'école et le volume effectif, matière par matière. Une ligne de la grille sans matière
/// correspondante dans l'établissement ressort sans <c>SubjectId</c> (« matière absente »), pour que le Directeur
/// voie ce que la grille attend.
/// </summary>
public record GetWeeklyHourNormsQuery(string GradeLevel, string? Series) : IRequest<WeeklyHourNormsDto>;

public class GetWeeklyHourNormsQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetWeeklyHourNormsQuery, WeeklyHourNormsDto>
{
    public async Task<WeeklyHourNormsDto> Handle(GetWeeklyHourNormsQuery request, CancellationToken cancellationToken)
    {
        var grade = request.GradeLevel;
        var series = HourVolumeRules.NormalizeSeries(request.Series);
        HourVolumeRules.EnsureValidScope(grade, series);

        var resolver = await WeeklyHourNormResolver.LoadAsync(dbContext, cancellationToken);
        var template = WeeklyHourTemplates.For(grade, series);
        var cycle = AgeNormTemplates.For(grade)!.Cycle;

        var subjects = (await dbContext.Subjects.AsNoTracking()
                .Where(s => s.ParentSubjectId == null)
                .Select(s => new { s.Id, s.Name, s.Level })
                .ToListAsync(cancellationToken))
            .Where(s => ClassroomCycle.CycleFor(s.Level) == cycle)
            .ToList();

        var rows = new List<WeeklyHourNormRow>();
        var matchedLines = new HashSet<HourTemplateLine>();

        foreach (var subject in subjects)
        {
            var line = template.FirstOrDefault(l => l.Matches(subject.Name));
            var own = resolver.Override(grade, series, subject.Id);
            var gradeWide = series is null ? null : resolver.Override(grade, null, subject.Id);
            if (line is null && own is null && gradeWide is null) continue;

            if (line is not null) matchedLines.Add(line);
            var effective = resolver.Resolve(grade, series, subject.Id, subject.Name);
            rows.Add(new WeeklyHourNormRow(
                subject.Id, subject.Name, line?.OptionGroup, line?.Hours, gradeWide?.WeeklyHours, own?.WeeklyHours,
                effective.Hours, own?.Id, own is null ? null : resolver.RowVersionOf(own.Id)));
        }

        rows.AddRange(template.Where(l => !matchedLines.Contains(l))
            .Select(l => new WeeklyHourNormRow(null, l.Label, l.OptionGroup, l.Hours, null, null, l.Hours, null, null)));

        var inputs = rows.Where(r => r.SubjectId is not null)
            .Select(r => new ComplianceInput(r.SubjectId!.Value, r.SubjectName, 0m, r.EffectiveHours, r.OptionGroup))
            .ToList();

        return new WeeklyHourNormsDto(grade, series, template.Count > 0, TimetableCompliance.Totals(inputs).NormHours,
            rows.OrderBy(r => r.SubjectId is null ? 1 : 0)
                .ThenBy(r => r.SubjectName, StringComparer.CurrentCultureIgnoreCase).ToList());
    }
}

public record ClassTimetableCompliance(
    Guid ClassroomId, string ClassroomName, string? GradeLevel, string? Series, ComplianceTotals Totals,
    IReadOnlyList<SubjectCompliance> Subjects, int ConflictCount);

public record TimetableComplianceDto(IReadOnlyList<ClassTimetableCompliance> Classes, IReadOnlyList<ScheduleConflict> Conflicts);

/// <summary>
/// GET /api/v1/hour-volumes/compliance[?classroomId=] — conformité des emplois du temps (Évolution N°7) : pour chaque
/// classe (ou la seule demandée), heures planifiées par matière contre le volume de référence de son niveau et de sa
/// série ; plus les chevauchements d'enseignant, de salle et de classe de tout l'établissement (ou ceux qui touchent
/// la classe demandée).
///
/// Matières contrôlées d'une classe : son programme (matières de classe actives, Évolution N°6) s'il est saisi, sinon
/// les matières du cycle qui ont un volume de référence ; plus, toujours, celles qui ont un créneau.
/// </summary>
public record GetTimetableComplianceQuery(Guid? ClassroomId) : IRequest<TimetableComplianceDto>;

public class GetTimetableComplianceQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTimetableComplianceQuery, TimetableComplianceDto>
{
    public async Task<TimetableComplianceDto> Handle(GetTimetableComplianceQuery request, CancellationToken cancellationToken)
    {
        var classrooms = await dbContext.Classrooms.AsNoTracking()
            .Where(c => request.ClassroomId == null || c.Id == request.ClassroomId)
            .Select(c => new { c.Id, c.Name, c.Cycle, c.Series })
            .ToListAsync(cancellationToken);

        if (request.ClassroomId is { } requested && classrooms.Count == 0)
        {
            throw new KeyNotFoundException($"Classe {requested} introuvable dans votre établissement.");
        }

        var slots = await (
            from s in dbContext.ScheduleSlots.AsNoTracking()
            join t in dbContext.Teachers.AsNoTracking() on s.TeacherId equals t.Id
            join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
            join sub in dbContext.Subjects.AsNoTracking() on s.SubjectId equals sub.Id
            select new SlotInfo(s.Id, s.DayOfWeek, s.StartTime, s.EndTime, s.TeacherId, t.FullName, s.ClassroomId, c.Name,
                s.SubjectId, sub.Name, s.RoomNumber))
            .ToListAsync(cancellationToken);

        var subjects = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.ParentSubjectId == null)
            .Select(s => new { s.Id, s.Name, s.Level })
            .ToListAsync(cancellationToken);
        var subjectNames = subjects.ToDictionary(s => s.Id, s => s.Name);

        var classSubjects = (await dbContext.ClassSubjects.AsNoTracking()
                .Where(cs => cs.IsActive)
                .Select(cs => new { cs.ClassroomId, cs.SubjectId, cs.OptionGroup })
                .ToListAsync(cancellationToken))
            .ToLookup(cs => cs.ClassroomId);

        var resolver = await WeeklyHourNormResolver.LoadAsync(dbContext, cancellationToken);

        var classIds = classrooms.Select(c => c.Id).ToHashSet();
        var conflicts = TimetableCompliance.DetectConflicts(slots)
            .Where(c => request.ClassroomId is null || classIds.Contains(c.First.ClassroomId) || classIds.Contains(c.Second.ClassroomId))
            .ToList();

        var result = new List<ClassTimetableCompliance>();
        foreach (var classroom in classrooms.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var grade = AgeRules.GradeOf(classroom.Name, classroom.Cycle);

            var planned = slots.Where(s => s.ClassroomId == classroom.Id)
                .GroupBy(s => s.SubjectId)
                .ToDictionary(g => g.Key, g => g.Sum(s => TimetableCompliance.HoursOf(s.StartTime, s.EndTime)));

            var programme = classSubjects[classroom.Id].ToList();
            var groups = programme.ToDictionary(p => p.SubjectId, p => p.OptionGroup);

            IEnumerable<Guid> candidates = programme.Count > 0
                ? programme.Select(p => p.SubjectId)
                : subjects.Where(s => ClassroomCycle.CycleFor(s.Level) == classroom.Cycle
                                      && resolver.Resolve(grade, classroom.Series, s.Id, s.Name).Hours is not null)
                    .Select(s => s.Id);

            var inputs = candidates.Concat(planned.Keys).Distinct()
                .Where(subjectNames.ContainsKey)
                .Select(id =>
                {
                    var norm = resolver.Resolve(grade, classroom.Series, id, subjectNames[id]);
                    return new ComplianceInput(id, subjectNames[id], planned.GetValueOrDefault(id), norm.Hours,
                        groups.GetValueOrDefault(id) ?? norm.OptionGroup);
                })
                .ToList();

            result.Add(new ClassTimetableCompliance(
                classroom.Id, classroom.Name, grade, classroom.Series, TimetableCompliance.Totals(inputs),
                TimetableCompliance.Evaluate(inputs),
                conflicts.Count(c => c.First.ClassroomId == classroom.Id || c.Second.ClassroomId == classroom.Id)));
        }

        return new TimetableComplianceDto(result, conflicts);
    }
}
