using SamaEcole.Application.Coefficients;

namespace SamaEcole.Application.HourVolumes;

/// <summary>
/// Une ligne d'un volume horaire de référence : la matière (libellé + alias reconnus), ses heures hebdomadaires et,
/// pour une matière AU CHOIX (LV2, langue ancienne), son groupe d'options — l'élève n'en suit qu'une.
/// </summary>
public sealed record HourTemplateLine(string Label, string[] Aliases, decimal Hours, string? OptionGroup = null)
{
    public bool Matches(string subjectName)
    {
        var name = SeriesCoefficientTemplates.NormalizeName(subjectName);
        return name.Length > 0
               && (SeriesCoefficientTemplates.NormalizeName(Label) == name
                   || Aliases.Any(a => SeriesCoefficientTemplates.NormalizeName(a) == name));
    }
}

/// <summary>
/// Volumes horaires hebdomadaires de référence (Évolution N°7) — données en code, comme les modèles de coefficients
/// (arbitrage A10) et les normes d'âge : l'école les ajuste par niveau, série et matière (weekly_hour_norms), jamais en
/// modifiant ce fichier.
///
/// Valeurs par défaut des grilles horaires du moyen (6e à 3e) et du secondaire général (Seconde S/L, Première et
/// Terminale S1, S2, L1a, L1b, L2) — indicatives, à rapprocher de l'arrêté en vigueur : c'est précisément pourquoi
/// l'établissement peut les régler. Aucune grille n'est codée pour le préscolaire, l'élémentaire (enseignement par
/// domaines, maître unique), les séries techniques et franco-arabes : le contrôle y est « sans référence » tant que
/// l'école ne saisit pas ses volumes — jamais un volume inventé.
/// </summary>
public static class WeeklyHourTemplates
{
    public const string SecondLanguageGroup = SeriesCoefficientTemplates.SecondLanguageGroup;
    public const string AncientLanguageGroup = SeriesCoefficientTemplates.AncientLanguageGroup;

    private static HourTemplateLine French(decimal h) => new("Français", [], h);
    private static HourTemplateLine English(decimal h) => new("Anglais", ["LV1", "Langue vivante 1", "Langue vivante I"], h);
    private static HourTemplateLine Maths(decimal h) => new("Mathématiques", ["Maths", "Math"], h);
    private static HourTemplateLine PhysicsChemistry(decimal h) => new("Physique-Chimie", ["PC", "Sciences physiques", "SP"], h);
    private static HourTemplateLine LifeSciences(decimal h) => new("SVT", ["Sciences de la vie et de la terre"], h);
    private static HourTemplateLine HistoryGeography(decimal h) =>
        new("Histoire-Géographie", ["Hist-Géo", "HG", "Histoire et Géographie"], h);
    private static HourTemplateLine Civics(decimal h) =>
        new("Éducation civique", ["EC", "Instruction civique", "Éducation à la citoyenneté"], h);
    private static HourTemplateLine Philosophy(decimal h) => new("Philosophie", ["Philo"], h);
    private static HourTemplateLine PhysicalEducation(decimal h) =>
        new("EPS", ["Éducation physique et sportive", "Sport"], h);

    private static readonly string[] Lv2Languages = ["Espagnol", "Allemand", "Arabe", "Italien", "Portugais"];

    private static IEnumerable<HourTemplateLine> SecondLanguage(decimal h)
        => Lv2Languages.Select(l => new HourTemplateLine(l, [], h, SecondLanguageGroup));

    private static IEnumerable<HourTemplateLine> AncientLanguage(decimal h) =>
    [
        new("Latin", [], h, AncientLanguageGroup),
        new("Grec", ["Grec ancien"], h, AncientLanguageGroup)
    ];

    private static IReadOnlyList<HourTemplateLine> Build(params object[] parts)
        => parts.SelectMany(p => p switch
        {
            HourTemplateLine line => [line],
            IEnumerable<HourTemplateLine> lines => lines,
            _ => throw new ArgumentException("Ligne de volume horaire invalide.", nameof(parts))
        }).ToList();

    private static IReadOnlyList<HourTemplateLine> CollegeFirstCycle() => Build(
        French(6), English(4), Maths(5), LifeSciences(2), HistoryGeography(3), Civics(1), PhysicalEducation(2));

    private static IReadOnlyList<HourTemplateLine> CollegeSecondCycle() => Build(
        French(5), English(4), SecondLanguage(3), Maths(5), PhysicsChemistry(3), LifeSciences(2),
        HistoryGeography(3), Civics(1), PhysicalEducation(2));

    private static IReadOnlyList<HourTemplateLine> FirstL1() => Build(
        French(5), Philosophy(3), AncientLanguage(4), English(3), SecondLanguage(3), HistoryGeography(4), Maths(2),
        PhysicalEducation(2));

    private static IReadOnlyList<HourTemplateLine> FinalL1() => Build(
        Philosophy(6), French(5), AncientLanguage(4), English(3), SecondLanguage(3), HistoryGeography(4), Maths(1),
        PhysicalEducation(2));

    /// <summary>Clé : (niveau, série). Série null : toutes les classes du niveau (collège).</summary>
    private static readonly Dictionary<(string Grade, string? Series), IReadOnlyList<HourTemplateLine>> ByScope = new()
    {
        [("Sixième", null)] = CollegeFirstCycle(),
        [("Cinquième", null)] = CollegeFirstCycle(),
        [("Quatrième", null)] = CollegeSecondCycle(),
        [("Troisième", null)] = CollegeSecondCycle(),

        // Seconde : grille de la FAMILLE de série (S… ou L…), faute de séries fines en Seconde.
        [("Seconde", "S")] = Build(
            Maths(6), PhysicsChemistry(5), LifeSciences(4), French(4), English(3), SecondLanguage(2),
            HistoryGeography(3), PhysicalEducation(2)),
        [("Seconde", "L")] = Build(
            French(5), English(4), SecondLanguage(4), HistoryGeography(4), Maths(3), PhysicsChemistry(2),
            LifeSciences(2), PhysicalEducation(2)),

        [("Première", "S1")] = Build(
            Maths(7), PhysicsChemistry(6), LifeSciences(2), French(4), Philosophy(3), English(3), HistoryGeography(3),
            PhysicalEducation(2)),
        [("Première", "S2")] = Build(
            LifeSciences(5), PhysicsChemistry(5), Maths(5), French(4), Philosophy(3), English(3), HistoryGeography(3),
            PhysicalEducation(2)),
        [("Première", "L2")] = Build(
            French(5), Philosophy(3), HistoryGeography(4), English(4), SecondLanguage(4), Maths(2), LifeSciences(2),
            PhysicalEducation(2)),
        [("Première", "L1A")] = FirstL1(),
        [("Première", "L1B")] = FirstL1(),

        [("Terminale", "S1")] = Build(
            Maths(8), PhysicsChemistry(8), LifeSciences(2), French(2), Philosophy(3), English(2), HistoryGeography(2),
            PhysicalEducation(2)),
        [("Terminale", "S2")] = Build(
            LifeSciences(6), PhysicsChemistry(6), Maths(5), French(2), Philosophy(3), English(2), HistoryGeography(2),
            PhysicalEducation(2)),
        [("Terminale", "L2")] = Build(
            Philosophy(7), French(5), HistoryGeography(5), English(4), SecondLanguage(3), Maths(2), LifeSciences(2),
            PhysicalEducation(2)),
        [("Terminale", "L1A")] = FinalL1(),
        [("Terminale", "L1B")] = FinalL1()
    };

    /// <summary>
    /// Grille de référence d'un niveau et d'une série : la série exacte, puis sa famille (« S », « L » — utile en
    /// Seconde), puis le niveau sans série. Liste vide si aucune grille n'est codée.
    /// </summary>
    public static IReadOnlyList<HourTemplateLine> For(string? gradeLevel, string? series)
    {
        if (gradeLevel is null) return [];

        if (series is not null)
        {
            if (ByScope.TryGetValue((gradeLevel, series), out var exact)) return exact;
            if (FamilyOf(series) is { } family && ByScope.TryGetValue((gradeLevel, family), out var byFamily)) return byFamily;
        }

        return ByScope.TryGetValue((gradeLevel, null), out var byGrade) ? byGrade : [];
    }

    /// <summary>La ligne de référence d'une matière (par son nom), ou null.</summary>
    public static HourTemplateLine? LineFor(string? gradeLevel, string? series, string subjectName)
        => For(gradeLevel, series).FirstOrDefault(l => l.Matches(subjectName));

    /// <summary>Famille d'une série du catalogue : « S » pour les scientifiques, « L » pour les littéraires, sinon null.</summary>
    public static string? FamilyOf(string? series)
    {
        var info = LyceeSeries.All.FirstOrDefault(s => s.Code == series);
        return info?.Category switch
        {
            LyceeSeries.Scientific => "S",
            LyceeSeries.Literary => "L",
            _ => null
        };
    }
}
