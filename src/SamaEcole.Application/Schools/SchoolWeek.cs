namespace SamaEcole.Application.Schools;

/// <summary>
/// Semaine de travail d'un établissement (Évolution N°3) : quels jours sont OUVRÉS, dans quel ordre on
/// les affiche, comment on les dit en français. Pur — aucun accès base — pour que chaque règle se
/// teste seule. Le réglage vit dans SchoolSettings.WorkingDays (texte : « Monday,Tuesday,… »).
/// </summary>
public static class SchoolWeek
{
    /// <summary>Lundi → Samedi : la grille historique. Voir SchoolSettingsDefaults.WorkingDays (à garder identique).</summary>
    public const string DefaultStored = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday";

    private static readonly DayOfWeek[] IsoOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private static readonly Dictionary<DayOfWeek, string> French = new()
    {
        [DayOfWeek.Monday] = "lundi", [DayOfWeek.Tuesday] = "mardi", [DayOfWeek.Wednesday] = "mercredi",
        [DayOfWeek.Thursday] = "jeudi", [DayOfWeek.Friday] = "vendredi",
        [DayOfWeek.Saturday] = "samedi", [DayOfWeek.Sunday] = "dimanche"
    };

    /// <summary>
    /// Noms → jours. `null` si la liste est vide, contient un nom inconnu, un nombre (« 3 » : on ne
    /// devine pas) ou un doublon — un réglage douteux ne doit jamais être stocké à moitié.
    /// </summary>
    public static IReadOnlyList<DayOfWeek>? TryParse(IEnumerable<string>? names)
    {
        if (names is null) return null;

        var days = new List<DayOfWeek>();
        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || int.TryParse(name, out _)) return null;
            if (!Enum.TryParse<DayOfWeek>(name, ignoreCase: true, out var day) || !Enum.IsDefined(day)) return null;
            if (days.Contains(day)) return null;
            days.Add(day);
        }

        return days.Count == 0 ? null : days;
    }

    /// <summary>Texte de la base → jours. Une valeur vide ou illisible retombe sur le défaut, jamais sur « aucun jour ».</summary>
    public static IReadOnlyList<DayOfWeek> FromStored(string? stored)
        => TryParse(stored?.Split(',', StringSplitOptions.RemoveEmptyEntries))
           ?? TryParse(DefaultStored.Split(','))!;

    /// <summary>Forme canonique stockée : lundi → dimanche, quel que soit l'ordre d'entrée.</summary>
    public static string Serialize(IEnumerable<DayOfWeek> days)
    {
        var set = days.ToHashSet();
        return string.Join(',', IsoOrder.Where(set.Contains));
    }

    /// <summary>
    /// Ordre d'AFFICHAGE : la semaine commence le lendemain du bloc de repos (repos jeudi/vendredi →
    /// samedi, dimanche, lundi, mardi, mercredi). Repos non contigus, ou aucun repos : lundi → dimanche.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> DisplayOrder(IEnumerable<DayOfWeek> days)
    {
        var set = days.ToHashSet();

        // « Fin de bloc de repos » : un jour de repos dont le lendemain est ouvré. Un seul = un bloc contigu.
        var blockEnds = Enumerable.Range(0, 7)
            .Where(i => !set.Contains(IsoOrder[i]) && set.Contains(IsoOrder[(i + 1) % 7]))
            .ToList();

        var start = blockEnds.Count == 1 ? (blockEnds[0] + 1) % 7 : 0;

        return Enumerable.Range(0, 7)
            .Select(offset => IsoOrder[(start + offset) % 7])
            .Where(set.Contains)
            .ToList();
    }

    public static IReadOnlyList<string> ToNames(IEnumerable<DayOfWeek> days)
        => DisplayOrder(days).Select(d => d.ToString()).ToList();

    public static bool IsWorkingDay(IEnumerable<DayOfWeek> days, DayOfWeek day) => days.Contains(day);

    public static bool IsWorkingDay(IEnumerable<DayOfWeek> days, DateOnly date) => days.Contains(date.DayOfWeek);

    public static string FrenchName(DayOfWeek day) => French[day];

    /// <summary>« jeudi et vendredi », « vendredi, samedi et dimanche », « aucun » si la semaine est pleine.</summary>
    public static string RestDaysLabel(IEnumerable<DayOfWeek> days)
    {
        var set = days.ToHashSet();
        var rest = IsoOrder.Where(d => !set.Contains(d)).Select(FrenchName).ToList();

        return rest.Count switch
        {
            0 => "aucun",
            1 => rest[0],
            _ => $"{string.Join(", ", rest.Take(rest.Count - 1))} et {rest[^1]}"
        };
    }
}
