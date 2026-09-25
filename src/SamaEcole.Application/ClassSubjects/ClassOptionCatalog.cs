using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.ClassSubjects;

/// <param name="ClassSubjectId">Identifiant à renvoyer pour retenir cette option.</param>
public sealed record OptionChoiceDto(Guid ClassSubjectId, Guid SubjectId, string SubjectName);

/// <param name="DefaultClassSubjectId">Option pré-cochée : la plus choisie dans l'établissement cette année, à défaut la première du groupe.</param>
/// <param name="SelectedClassSubjectId">Option retenue par l'élève cette année (lecture d'une fiche), null si aucune.</param>
public sealed record OptionGroupDto(
    string Name, IReadOnlyList<OptionChoiceDto> Options, Guid DefaultClassSubjectId, Guid? SelectedClassSubjectId);

/// <summary>
/// Groupes d'options d'une classe (Évolution N°6) — la section « Matières optionnelles » du formulaire
/// d'inscription et de la fiche élève, et la base de <see cref="StudentOptionWriter"/>. Seules les matières
/// ACTIVES d'un groupe sont proposées.
/// </summary>
public static class ClassOptionCatalog
{
    public static async Task<IReadOnlyList<OptionGroupDto>> LoadAsync(
        IApplicationDbContext dbContext, Guid classroomId, Guid? schoolYearId, Guid? studentId,
        CancellationToken cancellationToken)
    {
        var options = await (
            from c in dbContext.ClassSubjects.AsNoTracking()
            join s in dbContext.Subjects.AsNoTracking() on c.SubjectId equals s.Id
            where c.ClassroomId == classroomId && c.IsActive && c.OptionGroup != null
            orderby c.DisplayOrder, s.Name
            select new { c.Id, c.SubjectId, s.Name, Group = c.OptionGroup! })
            .ToListAsync(cancellationToken);

        if (options.Count == 0)
        {
            return [];
        }

        var popularity = schoolYearId is { } yearId
            ? await PopularityByNameAsync(dbContext, yearId, cancellationToken)
            : new Dictionary<string, int>();

        var selected = studentId is { } sid && schoolYearId is { } year
            ? (await dbContext.StudentSubjectEnrollments.AsNoTracking()
                    .Where(e => e.StudentId == sid && e.SchoolYearId == year)
                    .Select(e => e.ClassSubjectId)
                    .ToListAsync(cancellationToken))
                .ToHashSet()
            : [];

        return options
            .GroupBy(o => o.Group)
            .Select(g =>
            {
                var choices = g.Select(o => new OptionChoiceDto(o.Id, o.SubjectId, o.Name)).ToList();

                // La plus fréquente dans l'établissement ; à égalité (ou sans aucun choix encore), l'ordre du
                // groupe départage — l'ordre du modèle national.
                var byPopularity = choices
                    .Select((c, index) => (Choice: c, Index: index,
                        Count: popularity.GetValueOrDefault(SeriesCoefficientTemplates.NormalizeName(c.SubjectName))))
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Index)
                    .First();

                return new OptionGroupDto(
                    g.Key,
                    choices,
                    byPopularity.Choice.ClassSubjectId,
                    choices.Select(c => (Guid?)c.ClassSubjectId).FirstOrDefault(id => selected.Contains(id!.Value)));
            })
            .ToList();
    }

    /// <summary>
    /// Nombre d'élèves de l'établissement ayant retenu chaque matière optionnelle cette année, par NOM de matière :
    /// « Espagnol » de la L2 et « Espagnol » de la L1a sont la même langue, même s'ils sont deux matières en base.
    /// </summary>
    private static async Task<Dictionary<string, int>> PopularityByNameAsync(
        IApplicationDbContext dbContext, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var counts = await (
            from e in dbContext.StudentSubjectEnrollments.AsNoTracking()
            join c in dbContext.ClassSubjects.AsNoTracking() on e.ClassSubjectId equals c.Id
            join s in dbContext.Subjects.AsNoTracking() on c.SubjectId equals s.Id
            where e.SchoolYearId == schoolYearId
            group e by s.Name into g
            select new { Name = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts
            .GroupBy(c => SeriesCoefficientTemplates.NormalizeName(c.Name))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
    }
}
