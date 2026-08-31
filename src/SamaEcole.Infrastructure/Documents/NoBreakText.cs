namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Le tiret d'un identifiant métier (matricule "ELEV-2025-0005", reçu "REC-2025-0002", PV "PV-3F2A9C1D"…)
/// est un point de coupure de ligne légitime pour le moteur de rendu de texte de QuestPDF : sans
/// intervention, l'identifiant qui tombe en fin de ligne (surtout dans une colonne de tableau étroite)
/// peut se scinder sur deux lignes. Remplacer le tiret standard par un tiret insécable (U+2011) neutralise
/// cette coupure — l'identifiant reste toujours sur une seule ligne, quelle que soit la largeur disponible.
/// Utilisé partout où un tel identifiant s'imprime, pas seulement pour le matricule élève d'origine.
/// </summary>
internal static class NoBreakText
{
    /// <summary>
    /// <paramref name="value"/> null ou vide ressort en chaîne vide : un identifiant manquant ne doit
    /// jamais faire échouer QuestPDF (NRE dans <c>Compose</c>) et donc l'émission de tout le document.
    /// </summary>
    public static string NoBreak(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace('-', '‑');
}
