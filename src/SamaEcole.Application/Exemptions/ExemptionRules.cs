namespace SamaEcole.Application.Exemptions;

/// <summary>Une matière dispensée d'un élève, avec son coefficient de base (le coefficient effectif est calculé par le résumé).</summary>
public sealed record ExemptSubject(Guid SubjectId, string Name, decimal Coefficient);

/// <summary>Une matière que l'on peut dispenser : obligatoire et autonome, dans la classe de l'élève.</summary>
public sealed record DispensableSubject(Guid SubjectId, string Name);

/// <summary>Une dispense demandée : la matière et son motif (obligatoire, imposé par <see cref="ExemptionRules"/>).</summary>
public sealed record SubjectExemptionInput(Guid SubjectId, string? Reason);

/// <summary>
/// Règles PURES (aucun accès base) de la dispense d'une matière obligatoire : motif obligatoire et borné, matière
/// dispensable, pas de doublon. Le motif peut être médical : les messages nomment la matière, jamais le motif.
/// </summary>
public static class ExemptionRules
{
    /// <summary>Longueur maximale du motif (colonne <c>Reason</c>, varchar(200)).</summary>
    public const int MaxReasonLength = 200;

    /// <summary>
    /// Deux niveaux sont le même niveau s'ils ne diffèrent que par la casse ou les espaces de bord — la même
    /// tolérance qu'<c>EvaluationStructureBuilder</c> : le niveau est un texte libre par école.
    /// </summary>
    public static bool LevelMatches(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Null si le motif est valide (non vide, 200 caractères au plus après trim), sinon le message.</summary>
    public static string? ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Le motif de la dispense est obligatoire.";
        }

        return reason.Trim().Length > MaxReasonLength
            ? $"Le motif ne peut pas dépasser {MaxReasonLength} caractères."
            : null;
    }

    /// <summary>
    /// Null si les dispenses sont valides : chaque matière est dispensable (jamais une matière hors classe, une
    /// option ou un domaine), n'est dispensée qu'une fois et porte un motif.
    /// </summary>
    public static string? Validate(
        IReadOnlyList<DispensableSubject> dispensable, IReadOnlyCollection<SubjectExemptionInput> exemptions)
    {
        var byId = dispensable.ToDictionary(s => s.SubjectId);

        if (exemptions.Any(e => !byId.ContainsKey(e.SubjectId)))
        {
            return "Une des matières indiquées ne peut pas être dispensée : ce n'est pas une matière obligatoire de la classe de l'élève.";
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
