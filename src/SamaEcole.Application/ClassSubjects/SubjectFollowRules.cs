namespace SamaEcole.Application.ClassSubjects;

/// <summary>Ce que la règle de suivi a besoin de savoir d'une matière au programme d'une classe.</summary>
public sealed record ClassSubjectRule(Guid ClassSubjectId, Guid SubjectId, bool IsActive, string? OptionGroup);

/// <summary>
/// Quelles matières un élève SUIT-il (Évolution N°6) ? Pur — aucun accès base, testé seul.
///
/// Une matière de la classe n'est PAS suivie si elle est désactivée, ou si elle appartient à un groupe
/// d'options et que l'élève ne l'a pas choisie. Tout le reste est suivi — y compris une matière notée qui
/// n'a pas de ligne au programme de la classe : une classe jamais configurée garde exactement le comportement
/// d'avant (le bulletin imprime toute matière notée).
/// </summary>
public static class SubjectFollowRules
{
    /// <summary>Les matières de la classe que l'élève ne suit pas : elles sortent de sa grille de saisie et de son bulletin.</summary>
    public static IReadOnlySet<Guid> ExcludedSubjects(
        IEnumerable<ClassSubjectRule> classSubjects, IReadOnlySet<Guid> chosenClassSubjectIds)
        => classSubjects
            .Where(c => !c.IsActive || (c.OptionGroup is not null && !chosenClassSubjectIds.Contains(c.ClassSubjectId)))
            .Select(c => c.SubjectId)
            .ToHashSet();

    /// <summary>Forme stockée d'un nom de groupe : espaces de bord retirés ; vide → null (matière suivie par tous).</summary>
    public static string? NormalizeGroup(string? group)
        => string.IsNullOrWhiteSpace(group) ? null : group.Trim();
}
