using SamaEcole.Application.Common;

namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Une ligne d'un modèle national : la matière (libellé + alias reconnus), son coefficient et, pour une
/// matière AU CHOIX, son groupe d'options.
/// </summary>
/// <param name="OptionGroup">
/// Groupe d'options (Évolution N°6) : « LV2 », « Option scientifique »… Les lignes d'un même groupe sont des
/// ALTERNATIVES — l'élève en suit une seule, choisie à l'inscription. Null pour une matière suivie par toute
/// la classe.
/// </param>
public sealed record TemplateLine(string Label, string[] Aliases, decimal Coefficient, string? OptionGroup = null)
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
/// Source : référentiel des séries du Baccalauréat sénégalais transmis le 25/09/2026 (Évolution N°6), qui
/// remplace pour L2, S1 et S2 la table validée le 24/09/2026. L1 et TECH, codes de l'ancienne nomenclature,
/// gardent leur comportement : L1 son modèle du 24/09, TECH aucun modèle (<see cref="For"/> renvoie une liste
/// VIDE, et l'action « Appliquer le modèle » le dit clairement — jamais des coefficients inventés).
///
/// Ces modèles ne servent JAMAIS de repli au calcul (arbitrage A5) : ils sont matérialisés en surcharges de
/// série (ApplySeriesTemplateCommand) et en matières de classe (ClassSubjectTemplateInjector), donc visibles et
/// modifiables par le Directeur.
/// </summary>
public static class SeriesCoefficientTemplates
{
    public const string SecondLanguageGroup = "LV2";
    public const string AncientLanguageGroup = "Langue ancienne";
    public const string ScienceOptionGroup = "Option scientifique";
    public const string PhilosophyOrTheologyGroup = "Philosophie ou Théologie";

    // Alias : les écritures courantes d'une même matière dans les établissements sénégalais. Aucun alias ne
    // peut désigner deux lignes d'une même série (SeriesCoefficientTemplatesTests le vérifie).
    private static TemplateLine French(decimal c) => new("Français", [], c);
    private static TemplateLine Philosophy(decimal c, string? group = null) => new("Philosophie", ["Philo"], c, group);
    private static TemplateLine HistoryGeography(decimal c) =>
        new("Histoire-Géographie", ["Hist-Géo", "HG", "Histoire et Géographie"], c);

    // LV1 : l'anglais dans toutes les séries du référentiel.
    private static TemplateLine English(decimal c) =>
        new("Anglais", ["LV1", "Langue vivante 1", "Langue vivante I"], c);

    private static TemplateLine Maths(decimal c) => new("Mathématiques", ["Maths", "Math"], c);
    private static TemplateLine PhysicsChemistry(decimal c, string? group = null) =>
        new("Physique-Chimie", ["PC", "Sciences physiques", "SP"], c, group);
    private static TemplateLine LifeSciences(decimal c, string? group = null) =>
        new("SVT", ["Sciences de la vie et de la terre"], c, group);
    private static TemplateLine PhysicalEducation(decimal c) =>
        new("EPS", ["Éducation physique et sportive", "Sport"], c);
    private static TemplateLine Arabic(decimal c, string? group = null) => new("Arabe", ["Langue arabe"], c, group);
    private static TemplateLine Theology(decimal c, string? group = null) =>
        new("Théologie", ["Études islamiques", "Sciences islamiques"], c, group);

    /// <summary>
    /// LV2 : une ligne PAR LANGUE proposée, toutes au même coefficient, dans le groupe « LV2 » — l'élève en
    /// suit une. <paramref name="languages"/> exclut la langue déjà enseignée comme matière principale (l'arabe
    /// en série LA).
    /// </summary>
    private static IEnumerable<TemplateLine> SecondLanguage(decimal c, params string[] languages)
        => languages.Select(l => new TemplateLine(l, [], c, SecondLanguageGroup));

    private static readonly string[] Lv2Languages = ["Espagnol", "Allemand", "Arabe", "Italien"];

    private static IReadOnlyList<TemplateLine> Build(params object[] parts)
        => parts.SelectMany(p => p switch
        {
            TemplateLine line => [line],
            IEnumerable<TemplateLine> lines => lines,
            _ => throw new ArgumentException("Ligne de modèle invalide.", nameof(parts))
        }).ToList();

    private static IReadOnlyList<TemplateLine> LiteraryL1(decimal ancientLanguage) => Build(
        French(5), Philosophy(5),
        new TemplateLine("Latin", [], ancientLanguage, AncientLanguageGroup),
        new TemplateLine("Grec", ["Grec ancien"], ancientLanguage, AncientLanguageGroup),
        English(3), HistoryGeography(3), SecondLanguage(2, Lv2Languages), Maths(1), PhysicalEducation(1));

    private static IReadOnlyList<TemplateLine> AgronomyBiology() => Build(
        new TemplateLine("Biologie / Agronomie", ["Biologie", "Agronomie", "Biologie-Agro"], 6),
        PhysicsChemistry(5), Maths(4), French(3), Philosophy(2), English(2), HistoryGeography(2), PhysicalEducation(1));

    private static IReadOnlyList<TemplateLine> Technology() => Build(
        new TemplateLine("Matières technologiques",
            ["Matières technologiques principales", "Technologie", "Enseignements technologiques"], 8),
        Maths(5), PhysicsChemistry(5), French(3), Philosophy(2), English(2), PhysicalEducation(1));

    private static IReadOnlyList<TemplateLine> FrancoArabicScience(TemplateLine mainScience) => Build(
        mainScience, PhysicsChemistry(6), Arabic(4), French(3),
        Philosophy(2, PhilosophyOrTheologyGroup), Theology(2, PhilosophyOrTheologyGroup),
        HistoryGeography(2), PhysicalEducation(1));

    private static readonly Dictionary<string, IReadOnlyList<TemplateLine>> ByCode = new()
    {
        // ── Séries littéraires ──────────────────────────────────────────────────────────────────
        ["L1A"] = LiteraryL1(4),
        ["L1B"] = LiteraryL1(4),
        ["L'1"] = Build(
            French(5), Philosophy(5), English(4), SecondLanguage(4, Lv2Languages), HistoryGeography(3),
            new TemplateLine("LV3 / Option", ["LV3", "Langue vivante 3"], 2),
            Maths(1), PhysicalEducation(1)),
        ["L2"] = Build(
            French(5), Philosophy(5), HistoryGeography(5), English(3), SecondLanguage(2, Lv2Languages),
            Maths(2), LifeSciences(2, ScienceOptionGroup), PhysicsChemistry(2, ScienceOptionGroup),
            PhysicalEducation(1)),

        // ── Séries scientifiques ────────────────────────────────────────────────────────────────
        ["S1"] = Build(
            Maths(8), PhysicsChemistry(8), French(3), Philosophy(2), LifeSciences(2), English(2),
            HistoryGeography(2), PhysicalEducation(1)),
        ["S2"] = Build(
            LifeSciences(6), PhysicsChemistry(5), Maths(5), French(3), Philosophy(2), English(2),
            HistoryGeography(2), PhysicalEducation(1)),
        ["S3"] = Build(
            Maths(6), PhysicsChemistry(6),
            new TemplateLine("Construction / Dessin", ["Construction mécanique", "Dessin technique", "Dessin"], 5),
            French(3), Philosophy(2), English(2), HistoryGeography(2), PhysicalEducation(1)),
        ["S4"] = AgronomyBiology(),
        ["S5"] = AgronomyBiology(),

        // ── Séries tertiaires et techniques ─────────────────────────────────────────────────────
        ["STEG"] = Build(
            new TemplateLine("Comptabilité et Gestion", ["Comptabilité & Gestion", "Comptabilité", "Techniques de gestion"], 6),
            new TemplateLine("Économie et Organisation", ["Éco/Orga", "Économie", "Économie générale", "Organisation des entreprises"], 4),
            new TemplateLine("Mathématiques appliquées", ["Maths appliquées", "Mathématiques", "Maths"], 4),
            new TemplateLine("Droit", [], 3),
            French(3), English(3), Philosophy(2), HistoryGeography(2), PhysicalEducation(1)),
        // Le référentiel donne « Maths (5-6) » : la borne basse est retenue, le Directeur l'ajuste par classe.
        ["T1"] = Technology(),
        ["T2"] = Technology(),
        ["STIDD"] = Technology(),

        // ── Séries franco-arabes ────────────────────────────────────────────────────────────────
        ["LA"] = Build(
            Arabic(6), Theology(4), French(4), Philosophy(4), HistoryGeography(3),
            SecondLanguage(2, "Anglais", "Espagnol", "Allemand", "Italien"),
            Maths(1), PhysicalEducation(1)),
        // « Matières scientifiques (7-8) » : Mathématiques 8 en S1A, SVT 7 en S2A.
        ["S1A"] = FrancoArabicScience(Maths(8)),
        ["S2A"] = FrancoArabicScience(LifeSciences(7)),

        // ── Ancienne nomenclature (Évolution N°4) — conservée telle quelle ───────────────────────
        ["L1"] = Build(
            French(5), Philosophy(5),
            new TemplateLine("LV2", ["Langue vivante 2", "Langue vivante II", "Espagnol", "Allemand", "Portugais"], 4),
            HistoryGeography(3), new TemplateLine("Anglais", [], 3),
            Maths(1), PhysicsChemistry(1), LifeSciences(1), PhysicalEducation(1))
    };

    /// <summary>Le modèle d'une série ; liste vide pour une série sans modèle (TECH) ou inconnue.</summary>
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
