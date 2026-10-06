using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Schools;

/// <summary>
/// Cycles que l'établissement GÈRE (Maternelle, Primaire, Collège, Lycée) — ce que proposent les écrans de
/// création de classe, d'inscription et d'examens. Pur — aucun accès base — pour que chaque règle se teste
/// seule. Le réglage vit dans <c>SchoolSettings.ManagedCycles</c> (texte : « Maternelle,Primaire,… »), comme
/// <see cref="SchoolWeek"/> pour les jours ouvrés.
///
/// Les valeurs sont les noms de <see cref="CycleType"/>. Il n'y a PAS de cycle « Crèche » : Crèche et
/// Maternelle sont deux NIVEAUX du cycle <see cref="CycleType.Maternelle"/> (voir ClassroomCycle.CycleFor).
/// </summary>
public static class ManagedCycleSet
{
    /// <summary>Tous les cycles, dans l'ordre canonique de stockage et d'affichage. À garder identique à SchoolSettingsDefaults.ManagedCycles.</summary>
    public const string AllStored = "Maternelle,Primaire,College,Lycee";

    private static readonly CycleType[] CanonicalOrder =
        [CycleType.Maternelle, CycleType.Primaire, CycleType.College, CycleType.Lycee];

    /// <summary>Tous les cycles, dans l'ordre canonique.</summary>
    public static IReadOnlyList<CycleType> All => CanonicalOrder;

    /// <summary>Cycles d'un établissement Élémentaire / Primaire : ni Collège ni Lycée.</summary>
    public static IReadOnlyList<CycleType> Elementary => [CycleType.Maternelle, CycleType.Primaire];

    /// <summary>
    /// Noms → cycles. <c>null</c> si la liste est vide, contient un nom inconnu, un nombre (« 1 » : on ne
    /// devine pas — <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> l'accepterait) ou un doublon : un
    /// réglage douteux ne doit jamais être stocké à moitié.
    /// </summary>
    public static IReadOnlyList<CycleType>? TryParse(IEnumerable<string>? names)
    {
        if (names is null) return null;

        var cycles = new List<CycleType>();
        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || int.TryParse(name, out _)) return null;
            if (!Enum.TryParse<CycleType>(name, ignoreCase: true, out var cycle) || !Enum.IsDefined(cycle)) return null;
            if (cycles.Contains(cycle)) return null;
            cycles.Add(cycle);
        }

        return cycles.Count == 0 ? null : cycles;
    }

    /// <summary>
    /// Texte de la base → cycles. Une valeur vide ou illisible retombe sur TOUS les cycles, jamais sur « aucun » :
    /// on n'affiche jamais moins que ce que l'école avait.
    /// </summary>
    public static IReadOnlyList<CycleType> FromStored(string? stored)
        => TryParse(stored?.Split(',', StringSplitOptions.RemoveEmptyEntries)) is { } cycles
            ? Canonicalize(cycles)
            : CanonicalOrder;

    /// <summary>Forme canonique stockée : Maternelle → Lycée, quel que soit l'ordre d'entrée.</summary>
    public static string Serialize(IEnumerable<CycleType> cycles)
    {
        var set = cycles.ToHashSet();
        return string.Join(',', CanonicalOrder.Where(set.Contains));
    }

    /// <summary>Noms des cycles, dans l'ordre canonique (ce que renvoie l'API).</summary>
    public static IReadOnlyList<string> ToNames(IEnumerable<CycleType> cycles)
    {
        var set = cycles.ToHashSet();
        return CanonicalOrder.Where(set.Contains).Select(c => c.ToString()).ToList();
    }

    public static bool Contains(IEnumerable<CycleType> cycles, CycleType cycle) => cycles.Contains(cycle);

    /// <summary>Cycles à retirer pour passer de <paramref name="current"/> à <paramref name="requested"/>, dans l'ordre canonique.</summary>
    public static IReadOnlyList<CycleType> Removed(IEnumerable<CycleType> current, IEnumerable<CycleType> requested)
    {
        var kept = requested.ToHashSet();
        return CanonicalOrder.Where(c => current.Contains(c) && !kept.Contains(c)).ToList();
    }

    /// <summary>Libellé français d'un cycle, tel que l'affiche l'écran (« Maternelle / Crèche » : deux niveaux d'un même cycle).</summary>
    public static string FrenchLabel(CycleType cycle) => cycle switch
    {
        CycleType.Maternelle => "Maternelle / Crèche",
        CycleType.Primaire => "Primaire",
        CycleType.College => "Collège",
        CycleType.Lycee => "Lycée",
        _ => cycle.ToString()
    };

    private static IReadOnlyList<CycleType> Canonicalize(IEnumerable<CycleType> cycles)
    {
        var set = cycles.ToHashSet();
        return CanonicalOrder.Where(set.Contains).ToList();
    }
}
