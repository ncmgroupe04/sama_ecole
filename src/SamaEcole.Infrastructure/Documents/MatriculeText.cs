namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Le tiret d'un matricule (ex. "ELEV-2025-0005") est un point de coupure de ligne légitime pour le
/// moteur de rendu de texte de QuestPDF : sans intervention, un matricule qui tombe en fin de ligne
/// peut se scinder sur deux lignes. Remplacer le tiret standard par un tiret insécable (U+2011) neutralise
/// cette coupure — le matricule reste toujours sur une seule ligne, quelle que soit la largeur disponible.
/// </summary>
internal static class MatriculeText
{
    public static string NoBreak(string matricule) => matricule.Replace('-', '‑');
}
