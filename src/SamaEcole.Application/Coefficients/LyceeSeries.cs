namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Catalogue FERMÉ des séries de lycée (Évolution N°4, arbitrage A2). Fermé à dessein : une série
/// est la clé des surcharges et des modèles, un texte libre (« s2 », « S 2 ») les casserait. Ajouter une
/// série = une ligne ici et un test.
/// </summary>
public static class LyceeSeries
{
    public static readonly IReadOnlyList<(string Code, string Label)> All =
    [
        ("L1", "Série L1"),
        ("L2", "Série L2"),
        ("S1", "Série S1"),
        ("S2", "Série S2"),
        ("TECH", "Séries techniques (outils)")
    ];

    public static string? Normalize(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    public static bool IsValid(string? code) => code is not null && All.Any(s => s.Code == code);

    public static string LabelOf(string code) => All.First(s => s.Code == code).Label;
}
