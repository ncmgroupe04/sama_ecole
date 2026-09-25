namespace SamaEcole.Application.OptionalSubjects;

/// <summary>Une matière d'un niveau, telle que les règles la voient (aucune dépendance EF) : option ou obligatoire.</summary>
public sealed record LevelSubject(Guid Id, string Name, string? Group);

/// <summary>Dispense d'une matière OBLIGATOIRE : la matière et son motif (obligatoire, imposé par les règles).</summary>
public sealed record MandatoryExemption(Guid SubjectId, string? Reason);

/// <summary>
/// Règles PURES (aucun accès base) des matières optionnelles et des dispenses : normalisation d'un groupe,
/// appariement de niveau, validation d'un choix d'options et d'une dispense de matière obligatoire
/// (spécification §4.3, §5.2).
///
/// Un « choix » d'options est l'ensemble des options que l'élève SUIT. Ses dispenses d'options sont toutes
/// les autres options du niveau. Un choix vide dispense donc de toutes les options — c'est le cas d'un élève
/// qui n'en suit aucune, distinct de « aucun choix enregistré » (aucune ligne : il suit tout).
/// </summary>
public static class OptionSelectionRules
{
    /// <summary>Longueur maximale du motif d'une dispense (colonne <c>Reason</c>, varchar(200)).</summary>
    public const int MaxReasonLength = 200;

    /// <summary>Groupe nettoyé : blanc → null, espaces de bord retirés.</summary>
    public static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    /// <summary>
    /// Deux niveaux sont le même niveau s'ils ne diffèrent que par la casse ou les espaces de bord — la
    /// même tolérance qu'<c>EvaluationStructureBuilder</c> : le niveau est un texte libre par école.
    /// </summary>
    public static bool LevelMatches(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Null si le choix d'options est valide, sinon le message (français) à renvoyer en 422.</summary>
    public static string? Validate(IReadOnlyList<LevelSubject> levelOptions, IReadOnlyCollection<Guid> chosen)
    {
        var byId = levelOptions.ToDictionary(o => o.Id);
        var distinct = chosen.Distinct().ToList();

        if (distinct.Any(id => !byId.ContainsKey(id)))
        {
            return "Une des matières choisies n'est pas une option du niveau de cette classe.";
        }

        // Au plus une matière par groupe. Les options sans groupe sont cumulables : jamais comptées ici.
        var tooMany = distinct
            .Select(id => byId[id])
            .Where(o => NormalizeGroup(o.Group) is not null)
            .GroupBy(o => NormalizeGroup(o.Group)!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        return tooMany is null
            ? null
            : $"Une seule matière peut être suivie dans le groupe « {tooMany.Key} » "
              + $"({string.Join(", ", tooMany.Select(o => o.Name))}).";
    }

    /// <summary>Les options du niveau que l'élève ne suit pas : ce sont ses dispenses d'options.</summary>
    public static IReadOnlySet<Guid> ExemptedSubjectIds(
        IReadOnlyList<LevelSubject> levelOptions, IReadOnlyCollection<Guid> chosen)
    {
        var followed = chosen.ToHashSet();
        return levelOptions.Where(o => !followed.Contains(o.Id)).Select(o => o.Id).ToHashSet();
    }

    /// <summary>Le motif d'une dispense de matière obligatoire est obligatoire (non vide) et borné.</summary>
    public static string? ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Le motif de la dispense est obligatoire pour une matière obligatoire.";
        }

        return reason.Trim().Length > MaxReasonLength
            ? $"Le motif ne peut pas dépasser {MaxReasonLength} caractères."
            : null;
    }

    /// <summary>
    /// Null si les dispenses de matières OBLIGATOIRES sont valides : chaque matière est une matière
    /// obligatoire du niveau (jamais une option, jamais une matière d'un autre niveau), n'est dispensée
    /// qu'une fois, et porte un motif.
    /// </summary>
    public static string? ValidateMandatoryExemptions(
        IReadOnlyList<LevelSubject> levelMandatory, IReadOnlyCollection<MandatoryExemption> exemptions)
    {
        var byId = levelMandatory.ToDictionary(s => s.Id);

        if (exemptions.Any(e => !byId.ContainsKey(e.SubjectId)))
        {
            return "Une des matières dispensées n'est pas une matière obligatoire du niveau de cette classe.";
        }

        if (exemptions.GroupBy(e => e.SubjectId).Any(g => g.Count() > 1))
        {
            return "Une matière ne peut être dispensée qu'une seule fois.";
        }

        foreach (var exemption in exemptions)
        {
            if (ValidateReason(exemption.Reason) is { } message)
            {
                return $"{byId[exemption.SubjectId].Name} : {message}";
            }
        }

        return null;
    }
}
