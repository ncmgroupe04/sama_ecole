using SamaEcole.Application.Common;

namespace SamaEcole.Application.Coefficients;

/// <summary>Une ligne d'un modèle national : la matière (libellé + alias reconnus) et son coefficient.</summary>
public sealed record TemplateLine(string Label, string[] Aliases, decimal Coefficient)
{
    /// <summary>Vrai si <paramref name="subjectName"/> désigne cette matière (casse, accents, ponctuation ignorés).</summary>
    public bool Matches(string subjectName)
    {
        var name = SeriesCoefficientTemplates.NormalizeName(subjectName);
        return name.Length > 0
               && (SeriesCoefficientTemplates.NormalizeName(Label) == name
                   || Aliases.Any(a => SeriesCoefficientTemplates.NormalizeName(a) == name));
    }
}

/// <summary>
/// Modèles nationaux de coefficients par série (Évolution N°4, arbitrage A10) — UNE table de données.
/// Corriger un coefficient national = modifier une ligne ici et le test qui la fige
/// (SeriesCoefficientTemplatesTests) ; rien d'autre.
///
/// Source : table validée par la direction le 24/09/2026 (L1, L2, S1, S2). Aucune valeur n'a été fournie
/// pour TECH (séries techniques) : <see cref="For"/> renvoie alors une liste VIDE, et l'action « Appliquer le
/// modèle » le dit clairement — jamais des coefficients inventés.
///
/// Ces modèles ne servent JAMAIS de repli au calcul (arbitrage A5) : ils sont matérialisés en surcharges de
/// série par ApplySeriesTemplateCommand, donc visibles et modifiables par le Directeur.
/// </summary>
public static class SeriesCoefficientTemplates
{
    // Alias : les écritures courantes d'une même matière dans les établissements sénégalais. LV2 reconnaît
    // aussi les langues vivantes usuelles, qui sont chacune « la LV2 » d'une école ; l'arabe n'en fait pas
    // partie (il peut relever d'une filière franco-arabe, hors du périmètre d'un modèle national).
    private static TemplateLine French(decimal c) => new("Français", [], c);
    private static TemplateLine Philosophy(decimal c) => new("Philosophie", ["Philo"], c);
    private static TemplateLine SecondLanguage(decimal c) =>
        new("LV2", ["Langue vivante 2", "Langue vivante II", "Espagnol", "Allemand", "Portugais"], c);
    private static TemplateLine HistoryGeography(decimal c) =>
        new("Histoire-Géographie", ["Hist-Géo", "HG", "Histoire et Géographie"], c);
    private static TemplateLine English(decimal c) => new("Anglais", [], c);
    private static TemplateLine Maths(decimal c) => new("Mathématiques", ["Maths", "Math"], c);
    private static TemplateLine PhysicsChemistry(decimal c) =>
        new("Physique-Chimie", ["PC", "Sciences physiques", "SP"], c);
    private static TemplateLine LifeSciences(decimal c) =>
        new("SVT", ["Sciences de la vie et de la terre"], c);
    private static TemplateLine PhysicalEducation(decimal c) =>
        new("EPS", ["Éducation physique et sportive", "Sport"], c);

    private static readonly Dictionary<string, IReadOnlyList<TemplateLine>> ByCode = new()
    {
        ["L1"] =
        [
            French(5), Philosophy(5), SecondLanguage(4), HistoryGeography(3), English(3),
            Maths(1), PhysicsChemistry(1), LifeSciences(1), PhysicalEducation(1)
        ],
        ["L2"] =
        [
            French(4), Philosophy(4), HistoryGeography(3), English(3),
            Maths(2), SecondLanguage(2), PhysicsChemistry(1), LifeSciences(1), PhysicalEducation(1)
        ],
        ["S1"] =
        [
            Maths(6), PhysicsChemistry(6), French(2), Philosophy(2), HistoryGeography(2), English(2),
            LifeSciences(2), SecondLanguage(1), PhysicalEducation(1)
        ],
        ["S2"] =
        [
            Maths(5), PhysicsChemistry(5), LifeSciences(5), French(2), Philosophy(2), HistoryGeography(2),
            English(2), SecondLanguage(1), PhysicalEducation(1)
        ]
    };

    /// <summary>Le modèle d'une série ; liste vide pour une série sans modèle (TECH aujourd'hui) ou inconnue.</summary>
    public static IReadOnlyList<TemplateLine> For(string? series)
        => series is not null && ByCode.TryGetValue(series, out var lines) ? lines : [];

    /// <summary>
    /// Forme de COMPARAISON d'un nom de matière : majuscules, accents retirés, toute ponctuation ramenée à un
    /// espace, espaces répétés fondus — « Hist-Géo », « hist géo » et « HIST  GEO » se valent.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var folded = TextFolding.Fold(name);
        var spaced = new string(folded.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
