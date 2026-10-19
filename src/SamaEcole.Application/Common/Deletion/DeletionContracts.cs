namespace SamaEcole.Application.Common.Deletion;

/// <summary>
/// Un lien d'un élément à supprimer (spec 2026-10-17, §3). <paramref name="Breakable"/> : l'utilisateur peut le rompre
/// (commande « rupture des liens ») ; sinon il doit être traité ailleurs (ex. matériel prêté à restituer).
/// </summary>
public record DeletionLink(string Kind, string Label, int Count, bool Breakable, string BreakAction);

/// <summary>
/// Résultat de l'analyse avant suppression. <c>FinancialBlock</c> : une donnée financière active interdit la
/// suppression, sans option de rupture. <c>CanDelete</c> : plus aucun blocage ni lien.
/// </summary>
public record DeletionImpact(bool CanDelete, bool FinancialBlock, string? Message, IReadOnlyList<DeletionLink> Links);

/// <summary>Codes 409 stables destinés au client (AGENTS.md règle #9).</summary>
public static class DeletionErrorCodes
{
    public const string FinancialRecordsExist = "FINANCIAL_RECORDS_EXIST";
    public const string DependenciesExist = "DEPENDENCIES_EXIST";
    public const string DependenciesNotBreakable = "DEPENDENCIES_NOT_BREAKABLE";
}

public static class DeletionMessages
{
    /// <summary>Texte IMPOSÉ par le commanditaire — ne pas reformuler.</summary>
    public const string StudentFinancialBlock =
        "Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.";

    public static string CountLabel(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";

    /// <summary>« a », « a et b », « a, b et c ».</summary>
    public static string JoinLabels(IEnumerable<string> labels)
    {
        var list = labels.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " et " + list[^1]
        };
    }

    public static string DependenciesBlock(string head, IReadOnlyList<DeletionLink> links) =>
        $"{head} à {JoinLabels(links.Select(l => l.Label))}. "
        + "Vous devez rompre ces liens avant de pouvoir effectuer la suppression.";
}
