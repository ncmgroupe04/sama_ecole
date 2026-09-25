using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.OptionalSubjects;

/// <summary>
/// Passerelle entre la base et <see cref="OptionSelectionRules"/> : charge les matières d'un niveau puis
/// transforme un choix en dispenses, ou refuse (422). Partagée par la création d'inscription et par
/// <c>SetEnrollmentOptionsCommand</c> pour que les deux entrées appliquent exactement la même règle.
/// </summary>
public static class EnrollmentOptionsPlanner
{
    /// <summary>
    /// Matières optionnelles AUTONOMES du niveau (le niveau est un texte libre, comparé en mémoire sans
    /// casse ni espaces de bord — un WHERE SQL le ferait mal). Triées par groupe puis par nom.
    /// </summary>
    public static async Task<IReadOnlyList<LevelSubject>> LoadLevelOptionsAsync(
        IApplicationDbContext dbContext, string level, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.IsOptional && s.ParentSubjectId == null)
            .Select(s => new { s.Id, s.Name, s.Level, s.OptionGroup })
            .ToListAsync(cancellationToken);

        return rows
            .Where(s => OptionSelectionRules.LevelMatches(s.Level, level))
            .Select(s => new LevelSubject(s.Id, s.Name, OptionSelectionRules.NormalizeGroup(s.OptionGroup)))
            .OrderBy(o => o.Group ?? "￿", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(o => o.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Matières OBLIGATOIRES AUTONOMES du niveau : ni activité d'un domaine, ni domaine (une matière qui
    /// porte des activités n'est jamais notée, elle ne se dispense donc pas). Triées par nom.
    /// </summary>
    public static async Task<IReadOnlyList<LevelSubject>> LoadLevelMandatoryAsync(
        IApplicationDbContext dbContext, string level, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Subjects.AsNoTracking()
            .Where(s => !s.IsOptional
                        && s.ParentSubjectId == null
                        && !dbContext.Subjects.Any(child => child.ParentSubjectId == s.Id))
            .Select(s => new { s.Id, s.Name, s.Level })
            .ToListAsync(cancellationToken);

        return rows
            .Where(s => OptionSelectionRules.LevelMatches(s.Level, level))
            .Select(s => new LevelSubject(s.Id, s.Name, null))
            .OrderBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Les options à dispenser pour ce choix ; lève <see cref="ValidationException"/> sur <paramref name="field"/> si le choix est invalide.</summary>
    public static async Task<IReadOnlySet<Guid>> PlanExemptionsAsync(
        IApplicationDbContext dbContext, string level, IReadOnlyCollection<Guid> chosen, string field,
        CancellationToken cancellationToken)
    {
        var options = await LoadLevelOptionsAsync(dbContext, level, cancellationToken);

        if (OptionSelectionRules.Validate(options, chosen) is { } message)
        {
            throw new ValidationException([new ValidationFailure(field, message)]);
        }

        return OptionSelectionRules.ExemptedSubjectIds(options, chosen);
    }

    /// <summary>
    /// Valide les dispenses de matières obligatoires et renvoie celles-ci avec leur motif NETTOYÉ (espaces de
    /// bord retirés) ; lève <see cref="ValidationException"/> sur <paramref name="field"/> sinon.
    /// </summary>
    public static async Task<IReadOnlyList<MandatoryExemption>> PlanMandatoryExemptionsAsync(
        IApplicationDbContext dbContext, string level, IReadOnlyList<MandatoryExemption> exemptions, string field,
        CancellationToken cancellationToken)
    {
        var mandatory = await LoadLevelMandatoryAsync(dbContext, level, cancellationToken);

        if (OptionSelectionRules.ValidateMandatoryExemptions(mandatory, exemptions) is { } message)
        {
            throw new ValidationException([new ValidationFailure(field, message)]);
        }

        return exemptions.Select(e => new MandatoryExemption(e.SubjectId, e.Reason!.Trim())).ToList();
    }
}
