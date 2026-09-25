using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Coefficients.Commands;

/// <summary>
/// Source des modèles de coefficients. Une interface — et non un appel statique — pour que les tests
/// injectent un modèle de test au lieu de se coupler aux valeurs nationales, et pour qu'une future source
/// (fichier, base) ne touche aucun handler.
/// </summary>
public interface ISeriesTemplateProvider
{
    IReadOnlyList<TemplateLine> For(string series);
}

/// <summary>Modèles nationaux : la table de <see cref="SeriesCoefficientTemplates"/>.</summary>
public sealed class NationalSeriesTemplateProvider : ISeriesTemplateProvider
{
    public IReadOnlyList<TemplateLine> For(string series) => SeriesCoefficientTemplates.For(series);
}

/// <summary>
/// POST /api/v1/coefficients/apply-template — « Appliquer le modèle » (Évolution N°4, arbitrage A5). Écrit,
/// pour l'année ACTIVE, une surcharge de SÉRIE par matière reconnue dans le modèle de <see cref="Series"/>.
///
/// Ne modifie JAMAIS <c>Subject.Coefficient</c>, et ne touche que les surcharges de série (jamais celles d'une
/// classe). Sans <see cref="Overwrite"/>, une surcharge de série déjà posée est conservée ; avec, elle est
/// remplacée. Idempotente : rejouée, elle ne change plus rien.
/// </summary>
public record ApplySeriesTemplateCommand(string Series, bool Overwrite = false)
    : IRequest<ApplyTemplateResult>, IAuditableRequest;

/// <param name="Applied">Surcharges créées.</param>
/// <param name="Updated">Surcharges existantes remplacées (seulement avec <c>overwrite</c>, et seulement si la valeur change).</param>
/// <param name="SkippedExisting">Surcharges existantes conservées.</param>
/// <param name="UnmatchedTemplateLines">Lignes du modèle qu'aucune matière de l'école ne reconnaît (matière absente ou nommée autrement).</param>
/// <param name="UncoveredSubjects">Matières de l'école (hors primaire) qu'aucune ligne du modèle ne couvre.</param>
public record ApplyTemplateResult(
    int Applied,
    int Updated,
    int SkippedExisting,
    IReadOnlyList<string> UnmatchedTemplateLines,
    IReadOnlyList<string> UncoveredSubjects);

public class ApplySeriesTemplateCommandValidator : AbstractValidator<ApplySeriesTemplateCommand>
{
    public ApplySeriesTemplateCommandValidator()
        => RuleFor(x => x.Series)
            .Must(s => LyceeSeries.IsValid(LyceeSeries.Normalize(s)))
            .WithMessage(LyceeSeries.UnknownMessage);
}

public class ApplySeriesTemplateCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISeriesTemplateProvider templates)
    : IRequestHandler<ApplySeriesTemplateCommand, ApplyTemplateResult>
{
    public async Task<ApplyTemplateResult> Handle(ApplySeriesTemplateCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var series = LyceeSeries.Normalize(request.Series)!;
        var lines = templates.For(series);

        if (lines.Count == 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Series),
                    $"Aucun modèle national n'est disponible pour la {LyceeSeries.LabelOf(series).ToLowerInvariant()} : "
                    + "réglez ses coefficients matière par matière.")
            ]);
        }

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);

        // Le niveau d'une matière est un texte libre : on écarte seulement ce que le calcul neutralise de
        // toute façon (primaire, maternelle) — voir GetCoefficientGridQueryHandler.
        var subjects = (await dbContext.Subjects.AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.Level })
                .ToListAsync(cancellationToken))
            .Where(s => !CoefficientRules.IsPrimaryLevel(s.Level))
            .OrderBy(s => s.Level, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Suivies (tracking) : le remplacement d'une valeur passe par le verrou xmin normal d'EF.
        var existing = (await dbContext.SubjectCoefficientOverrides
                .Where(o => o.SchoolYearId == yearId && o.Series == series)
                .ToListAsync(cancellationToken))
            .ToDictionary(o => o.SubjectId);

        int applied = 0, updated = 0, skipped = 0;
        var matchedLines = new HashSet<TemplateLine>();
        var uncovered = new List<string>();

        foreach (var subject in subjects)
        {
            var line = lines.FirstOrDefault(l => l.Matches(subject.Name));
            if (line is null)
            {
                uncovered.Add($"{subject.Name} ({subject.Level})");
                continue;
            }

            matchedLines.Add(line);

            if (!existing.TryGetValue(subject.Id, out var row))
            {
                dbContext.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
                {
                    SchoolId = schoolId,
                    SchoolYearId = yearId,
                    SubjectId = subject.Id,
                    Series = series,
                    Coefficient = line.Coefficient
                });
                applied++;
            }
            else if (request.Overwrite && row.Coefficient != line.Coefficient)
            {
                row.Coefficient = line.Coefficient;
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ApplyTemplateResult(
            applied, updated, skipped,
            lines.Where(l => !matchedLines.Contains(l)).Select(l => l.Label).ToList(),
            uncovered);
    }
}
