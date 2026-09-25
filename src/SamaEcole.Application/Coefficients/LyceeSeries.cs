namespace SamaEcole.Application.Coefficients;

/// <summary>Une série du catalogue : code (clé stockée), libellé affiché, famille, et statut d'ancien code.</summary>
/// <param name="IsLegacy">
/// Code de l'ancienne nomenclature (Évolution N°4 : « L1 », « TECH ») : toujours VALIDE — des classes et des
/// surcharges déjà enregistrées le portent —, mais plus proposé pour une nouvelle classe.
/// </param>
public sealed record SeriesInfo(string Code, string Label, string Category, bool IsLegacy = false);

/// <summary>
/// Catalogue FERMÉ des séries de lycée (Évolution N°4, arbitrage A2 ; étendu au référentiel de l'Office du
/// Baccalauréat par l'Évolution N°6). Fermé à dessein : une série est la clé des surcharges et des modèles,
/// un texte libre (« s2 », « S 2 ») les casserait. Ajouter une série = une ligne ici, son modèle dans
/// <see cref="SeriesCoefficientTemplates"/> et un test.
///
/// Une classe SANS série (null) est une classe « Général / Collège » : Seconde commune, collège, primaire.
/// </summary>
public static class LyceeSeries
{
    public const string Literary = "Littéraire";
    public const string Scientific = "Scientifique";
    public const string Technical = "Technique";
    public const string FrancoArabic = "Franco-Arabe";

    /// <summary>Message de refus PARTAGÉ par tous les validateurs qui acceptent une série.</summary>
    public const string UnknownMessage =
        "Série inconnue : choisissez une série du catalogue (S1, S2, L1a, L2, STEG, T1, LA, S1A…).";

    public static readonly IReadOnlyList<SeriesInfo> All =
    [
        new("L1A", "Série L1a", Literary),
        new("L1B", "Série L1b", Literary),
        new("L'1", "Série L'1", Literary),
        new("L2", "Série L2", Literary),
        new("S1", "Série S1", Scientific),
        new("S2", "Série S2", Scientific),
        new("S3", "Série S3", Scientific),
        new("S4", "Série S4", Scientific),
        new("S5", "Série S5", Scientific),
        new("STEG", "Série STEG", Technical),
        new("T1", "Série T1", Technical),
        new("T2", "Série T2", Technical),
        new("STIDD", "Série STIDD", Technical),
        new("LA", "Série LA", FrancoArabic),
        new("S1A", "Série S1A", FrancoArabic),
        new("S2A", "Série S2A", FrancoArabic),
        new("L1", "Série L1 (ancienne nomenclature)", Literary, IsLegacy: true),
        new("TECH", "Séries techniques (ancienne nomenclature)", Technical, IsLegacy: true)
    ];

    /// <summary>
    /// Forme stockée d'un code saisi : espaces de bord retirés, majuscules, apostrophe typographique ramenée à
    /// l'apostrophe droite (« l’1 » → « L'1 »). Null pour une saisie vide.
    /// </summary>
    public static string? Normalize(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : code.Trim().Replace('’', '\'').Replace('‘', '\'').Replace('`', '\'').ToUpperInvariant();

    public static bool IsValid(string? code) => code is not null && All.Any(s => s.Code == code);

    public static string LabelOf(string code) => All.First(s => s.Code == code).Label;
}
