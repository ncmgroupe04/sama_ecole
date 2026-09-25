using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Coefficients.Queries;

/// <summary>
/// GET /api/v1/coefficients?series=S2 | ?classroomId={id} (+ schoolYearId, défaut : l'année active) — la
/// grille de l'écran « Coefficients » : une ligne par matière hors primaire/maternelle, avec sa valeur de
/// base, la surcharge de la portée demandée et le coefficient EFFECTIF (calculé par
/// <see cref="SubjectCoefficients.Resolve"/>, jamais par une seconde formule).
///
/// Lecture seule — Query et non Command (règle #7). L'école vient du JWT ; aucun filtre SchoolId à la main.
/// </summary>
public record GetCoefficientGridQuery(string? Series, Guid? ClassroomId, Guid? SchoolYearId = null)
    : IRequest<CoefficientGridDto>;

/// <param name="YearHasGrades">Vrai si des notes existent déjà sur l'année : l'écran avertit alors qu'un changement recalcule les bulletins (arbitrage A7).</param>
public record CoefficientGridDto(
    Guid SchoolYearId, string SchoolYearLabel, bool YearHasGrades, IReadOnlyList<CoefficientGridRow> Rows);

/// <param name="InheritedSeriesCoefficient">Portée « classe » seulement : la surcharge de la série de la classe, si elle existe (ce que la classe hériterait sans surcharge propre).</param>
/// <param name="Source">« Subject », « Series » ou « Classroom » : d'où vient <paramref name="EffectiveCoefficient"/>.</param>
public record CoefficientGridRow(
    Guid SubjectId,
    string SubjectName,
    string Level,
    decimal BaseCoefficient,
    Guid? OverrideId,
    decimal? OverrideCoefficient,
    uint? RowVersion,
    decimal? InheritedSeriesCoefficient,
    decimal EffectiveCoefficient,
    string Source);

public class GetCoefficientGridQueryValidator : AbstractValidator<GetCoefficientGridQuery>
{
    public GetCoefficientGridQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => (LyceeSeries.Normalize(x.Series) is not null) ^ (x.ClassroomId is not null))
            .OverridePropertyName("Scope")
            .WithMessage("Précisez soit une série, soit une classe — jamais les deux.");

        RuleFor(x => x.Series)
            .Must(s => LyceeSeries.IsValid(LyceeSeries.Normalize(s)))
            .When(x => LyceeSeries.Normalize(x.Series) is not null)
            .WithMessage(LyceeSeries.UnknownMessage);
    }
}

public class GetCoefficientGridQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetCoefficientGridQuery, CoefficientGridDto>
{
    public async Task<CoefficientGridDto> Handle(GetCoefficientGridQuery request, CancellationToken cancellationToken)
    {
        var yearId = request.SchoolYearId ?? await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);

        var yearLabel = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.Id == yearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.SchoolYearId), "L'année scolaire indiquée n'existe pas dans votre établissement.")
            ]);

        var series = LyceeSeries.Normalize(request.Series);

        // Série héritée par la classe demandée (portée « classe » uniquement).
        string? classroomSeries = null;
        if (request.ClassroomId is { } classroomId)
        {
            var classroom = await dbContext.Classrooms.AsNoTracking()
                .Where(c => c.Id == classroomId)
                .Select(c => new { c.Cycle, c.Series })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
                ]);

            if (classroom.Cycle.UsesSimplifiedGrading())
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.ClassroomId), CoefficientRules.PrimaryMessage)
                ]);
            }

            classroomSeries = classroom.Series;
        }

        // Le niveau d'une matière est un texte libre (« Terminale S2 » est lu comme un collège par
        // ClassroomCycle) : on ne peut pas filtrer « le lycée » par cycle, seulement écarter ce que le
        // calcul neutralise de toute façon — primaire et maternelle.
        var subjects = (await dbContext.Subjects.AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.Level, s.Coefficient })
                .ToListAsync(cancellationToken))
            .Where(s => !CoefficientRules.IsPrimaryLevel(s.Level))
            .OrderBy(s => s.Level, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var overrides = await dbContext.SubjectCoefficientOverrides.AsNoTracking()
            .Where(o => o.SchoolYearId == yearId
                        && (series != null ? o.Series == series
                            : o.ClassroomId == request.ClassroomId
                              || (classroomSeries != null && o.Series == classroomSeries)))
            .Select(o => new { o.Id, o.SubjectId, o.ClassroomId, o.Series, o.Coefficient, RowVersion = EF.Property<uint>(o, "xmin") })
            .ToListAsync(cancellationToken);

        var rows = subjects.Select(subject =>
        {
            // Ligne de LA portée demandée, et — pour une classe — celle de sa série.
            var own = overrides.FirstOrDefault(o => o.SubjectId == subject.Id
                && (series != null ? o.Series == series : o.ClassroomId == request.ClassroomId));
            var inherited = request.ClassroomId is null
                ? null
                : overrides.FirstOrDefault(o => o.SubjectId == subject.Id && o.ClassroomId is null);

            var classroomValue = request.ClassroomId is null ? null : own?.Coefficient;
            var seriesValue = series != null ? own?.Coefficient : inherited?.Coefficient;
            var effective = SubjectCoefficients.Resolve(subject.Coefficient, classroomValue, seriesValue);

            var source = classroomValue is not null ? CoefficientRules.SourceClassroom
                : seriesValue is not null ? CoefficientRules.SourceSeries
                : CoefficientRules.SourceSubject;

            return new CoefficientGridRow(
                subject.Id, subject.Name, subject.Level, subject.Coefficient,
                own?.Id, own?.Coefficient, own?.RowVersion,
                inherited?.Coefficient, effective, source);
        }).ToList();

        var yearHasGrades = await (
            from g in dbContext.Grades.AsNoTracking()
            join t in dbContext.Terms.AsNoTracking() on g.TermId equals t.Id
            where t.SchoolYearId == yearId
            select g.Id).AnyAsync(cancellationToken);

        return new CoefficientGridDto(yearId, yearLabel, yearHasGrades, rows);
    }
}
