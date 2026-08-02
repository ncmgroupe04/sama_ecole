using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.PublicDirectory;

/// <summary>
/// Libellés lisibles des cycles pour l'annuaire public. La base stocke l'énumération telle quelle
/// (« College », « Lycee » — sans accent, comme tout nom de membre C#) : servir ces valeurs brutes à
/// un parent afficherait « Lycee » sur une vitrine grand public.
///
/// Une valeur inconnue est renvoyée TELLE QUELLE plutôt que masquée ou remplacée : si un cycle est
/// ajouté à <see cref="CycleType"/> sans passer ici, l'annuaire affichera un libellé imparfait — pas
/// une fiche amputée d'un cycle réellement proposé.
/// </summary>
public static class CycleLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(CycleType.Maternelle)] = "Maternelle",
        [nameof(CycleType.Primaire)] = "Primaire",
        [nameof(CycleType.College)] = "Collège",
        [nameof(CycleType.Lycee)] = "Lycée"
    };

    public static string ToLabel(string rawCycle) =>
        Labels.TryGetValue(rawCycle, out var label) ? label : rawCycle;

    /// <summary>
    /// Traduit une liste de cycles bruts en libellés, dans l'ordre PÉDAGOGIQUE (du plus jeune au plus
    /// âgé) et non alphabétique : « Collège, Lycée, Primaire » n'a aucun sens pour un parent.
    /// </summary>
    public static IReadOnlyList<string> ToLabels(IEnumerable<string> rawCycles)
    {
        var order = new[]
        {
            nameof(CycleType.Maternelle),
            nameof(CycleType.Primaire),
            nameof(CycleType.College),
            nameof(CycleType.Lycee)
        };

        return [.. rawCycles
            .OrderBy(c =>
            {
                var index = Array.FindIndex(order, o => string.Equals(o, c, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index; // cycle inconnu : rejeté en fin de liste
            })
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(ToLabel)];
    }
}
