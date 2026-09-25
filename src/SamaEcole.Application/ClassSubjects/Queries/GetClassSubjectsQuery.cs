using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.ClassSubjects.Queries;

/// <summary>
/// GET /api/v1/class-subjects?classroomId={id} — l'onglet « Matières par classe » (Évolution N°6, étape B) : le
/// programme de la classe, chaque matière avec son coefficient EFFECTIF pour l'année active (calculé par
/// <see cref="SubjectCoefficients.Resolve"/>, jamais par une seconde formule), son groupe d'options et la valeur
/// officielle du modèle de la série. Lecture seule — Query (règle #7).
/// </summary>
public record GetClassSubjectsQuery(Guid ClassroomId) : IRequest<ClassSubjectsDto>;

/// <param name="HasTemplate">Vrai si la série de la classe a un modèle national : « Réinitialiser » est alors possible.</param>
/// <param name="SchoolYearId">Année active, à laquelle se rattachent coefficients et choix d'options ; null si aucune.</param>
public record ClassSubjectsDto(
    Guid ClassroomId,
    string ClassroomName,
    string? Series,
    string? SeriesLabel,
    bool HasTemplate,
    Guid? SchoolYearId,
    string? SchoolYearLabel,
    IReadOnlyList<ClassSubjectRowDto> Rows,
    IReadOnlyList<OptionGroupSummaryDto> OptionGroups);

/// <param name="RowVersion">Jeton xmin de la ligne de programme (désactivation, groupe d'options).</param>
/// <param name="Source">« Subject », « Series » ou « Classroom » : d'où vient <paramref name="EffectiveCoefficient"/>.</param>
/// <param name="OverrideId">Surcharge de CLASSE de l'année active, à corriger ou « Rétablir » via /coefficients.</param>
/// <param name="OfficialCoefficient">Coefficient du modèle national de la série pour cette matière ; null hors modèle.</param>
/// <param name="EnrolledStudents">Matière optionnelle : nombre d'élèves qui l'ont choisie cette année.</param>
public record ClassSubjectRowDto(
    Guid Id,
    Guid SubjectId,
    string SubjectName,
    string Level,
    string? OptionGroup,
    bool IsCustom,
    bool IsActive,
    int DisplayOrder,
    uint RowVersion,
    decimal BaseCoefficient,
    decimal EffectiveCoefficient,
    string Source,
    Guid? OverrideId,
    decimal? OverrideCoefficient,
    uint? OverrideRowVersion,
    decimal? OfficialCoefficient,
    int? EnrolledStudents);

/// <param name="StudentsWithoutChoice">Élèves de la classe sans option retenue dans ce groupe : ils n'apparaissent dans aucune grille de saisie du groupe.</param>
public record OptionGroupSummaryDto(string Name, Guid DefaultClassSubjectId, int StudentsWithoutChoice);

public class GetClassSubjectsQueryValidator : AbstractValidator<GetClassSubjectsQuery>
{
    public GetClassSubjectsQueryValidator() => RuleFor(x => x.ClassroomId).NotEmpty();
}

public class GetClassSubjectsQueryHandler(IApplicationDbContext dbContext, ISeriesTemplateProvider templates)
    : IRequestHandler<GetClassSubjectsQuery, ClassSubjectsDto>
{
    public async Task<ClassSubjectsDto> Handle(GetClassSubjectsQuery request, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : une classe d'une autre école est introuvable → 404.
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == request.ClassroomId)
            .Select(c => new { c.Id, c.Name, c.Series })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        var year = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => new { y.Id, y.Label })
            .FirstOrDefaultAsync(cancellationToken);

        var lines = classroom.Series is { } series ? templates.For(series) : [];

        var rows = await (
            from c in dbContext.ClassSubjects.AsNoTracking()
            join s in dbContext.Subjects.AsNoTracking() on c.SubjectId equals s.Id
            where c.ClassroomId == classroom.Id
            orderby c.DisplayOrder, s.Name
            select new
            {
                c.Id, c.SubjectId, s.Name, s.Level, s.Coefficient, c.OptionGroup, c.IsCustom, c.IsActive,
                c.DisplayOrder, RowVersion = EF.Property<uint>(c, "xmin")
            })
            .ToListAsync(cancellationToken);

        var overrides = year is null
            ? []
            : await dbContext.SubjectCoefficientOverrides.AsNoTracking()
                .Where(o => o.SchoolYearId == year.Id
                            && (o.ClassroomId == classroom.Id || (classroom.Series != null && o.Series == classroom.Series)))
                .Select(o => new { o.Id, o.SubjectId, o.ClassroomId, o.Coefficient, RowVersion = EF.Property<uint>(o, "xmin") })
                .ToListAsync(cancellationToken);

        var enrollments = year is null
            ? []
            : await (
                from e in dbContext.StudentSubjectEnrollments.AsNoTracking()
                join c in dbContext.ClassSubjects.AsNoTracking() on e.ClassSubjectId equals c.Id
                where c.ClassroomId == classroom.Id && e.SchoolYearId == year.Id
                select new { e.StudentId, e.ClassSubjectId })
                .ToListAsync(cancellationToken);

        var enrolledCount = enrollments.GroupBy(e => e.ClassSubjectId).ToDictionary(g => g.Key, g => g.Count());

        var dtoRows = rows.Select(r =>
        {
            var own = overrides.FirstOrDefault(o => o.SubjectId == r.SubjectId && o.ClassroomId == classroom.Id);
            var inherited = overrides.FirstOrDefault(o => o.SubjectId == r.SubjectId && o.ClassroomId is null);
            var effective = SubjectCoefficients.Resolve(r.Coefficient, own?.Coefficient, inherited?.Coefficient);
            var source = own is not null ? CoefficientRules.SourceClassroom
                : inherited is not null ? CoefficientRules.SourceSeries
                : CoefficientRules.SourceSubject;

            return new ClassSubjectRowDto(
                r.Id, r.SubjectId, r.Name, r.Level, r.OptionGroup, r.IsCustom, r.IsActive, r.DisplayOrder, r.RowVersion,
                r.Coefficient, effective, source,
                own?.Id, own?.Coefficient, own?.RowVersion,
                lines.FirstOrDefault(l => l.Matches(r.Name))?.Coefficient,
                r.OptionGroup is null ? null : enrolledCount.GetValueOrDefault(r.Id));
        }).ToList();

        var groups = await ClassOptionCatalog.LoadAsync(dbContext, classroom.Id, year?.Id, null, cancellationToken);

        // Élèves ACTUELS de la classe (ceux de la grille de saisie) sans choix dans chaque groupe.
        var studentIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == classroom.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var summaries = groups.Select(g =>
        {
            var optionIds = g.Options.Select(o => o.ClassSubjectId).ToHashSet();
            var withChoice = enrollments.Where(e => optionIds.Contains(e.ClassSubjectId)).Select(e => e.StudentId).ToHashSet();
            return new OptionGroupSummaryDto(g.Name, g.DefaultClassSubjectId, studentIds.Count(id => !withChoice.Contains(id)));
        }).ToList();

        return new ClassSubjectsDto(
            classroom.Id, classroom.Name, classroom.Series,
            classroom.Series is { } code && LyceeSeries.IsValid(code) ? LyceeSeries.LabelOf(code) : null,
            lines.Count > 0, year?.Id, year?.Label, dtoRows, summaries);
    }
}
