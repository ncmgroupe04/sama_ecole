namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Titre du bulletin, déduit du libellé de la période (« 1er semestre » → « BULLETIN DU 1ER SEMESTRE »).
/// « période » est féminin : « BULLETIN DE LA 1RE PÉRIODE ». Sans libellé, le titre historique.
/// Voir docs/design-references/README.md §2 (le titre suit le découpage choisi par l'école).
/// </summary>
internal static class BulletinTitle
{
    public const string Fallback = "BULLETIN DE NOTES";

    public static string For(string? termLabel)
    {
        if (string.IsNullOrWhiteSpace(termLabel))
        {
            return Fallback;
        }

        var upper = termLabel.Trim().ToUpperInvariant();

        return upper.Contains("PÉRIODE", StringComparison.Ordinal)
            ? $"BULLETIN DE LA {upper}"
            : $"BULLETIN DU {upper}";
    }
}
