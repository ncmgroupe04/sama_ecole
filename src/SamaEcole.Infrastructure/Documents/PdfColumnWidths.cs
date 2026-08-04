namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Largeurs (en points PDF) des colonnes de tableau dont le CONTENU a un gabarit fixe : un matricule,
/// une date, un numéro de téléphone ont toujours la même longueur, d'un élève à l'autre et d'un
/// document à l'autre. Une <c>RelativeColumn</c> les fait déborder dès que le tableau se resserre
/// (colonne voisine plus large, page portrait au lieu de paysage) : le dernier caractère repasse à la
/// ligne et la ligne du tableau double de hauteur. <c>NoBreakText</c> ne corrige pas ce cas — il
/// neutralise la coupure sur le tiret, pas le repli d'un texte plus large que sa colonne.
///
/// Ces constantes sont la SEULE source de vérité : un même matricule doit occuper la même largeur sur
/// la liste des élèves, la liste des enseignants, le PV de délibération et le journal de caisse.
/// Régler une largeur « à l'œil » dans un document isolé, c'est réintroduire la dérive que ce fichier
/// supprime. Valeurs calibrées au rendu (2026-08-04) sur la police par défaut de QuestPDF.
/// </summary>
internal static class PdfColumnWidths
{
    /// <summary>
    /// Identifiant métier : matricule (« ELEV‑2025‑0001 », « ENS‑2025‑0007 »), numéro de reçu
    /// (« REC‑2025‑0002 »), référence de PV. Calibrée sur 14 caractères à 8 pt.
    /// </summary>
    public const float Identifier = 78f;

    /// <summary>
    /// Date au format « jj/mm/aaaa » (naissance, embauche, paiement, échéance). Sans largeur fixe,
    /// c'est l'ANNÉE qui saute à la ligne — le défaut le plus visible sur une liste imprimée.
    /// </summary>
    public const float Date = 62f;

    /// <summary>
    /// Date suivie d'une heure (« 04/08/2026 14:30 ») — journaux, historiques, pointage.
    /// </summary>
    public const float DateTime = 88f;

    /// <summary>Heure seule (« 14:30 »).</summary>
    public const float Time = 38f;

    /// <summary>
    /// Téléphone mis en forme par <c>PhoneFormatter.FormatSenegal</c>. Dimensionnée sur la forme
    /// LONGUE avec indicatif (« +221 77 000 00 00 »), pas sur « 77 000 00 00 » : une école qui saisit
    /// l'indicatif verrait sinon la fin du numéro passer à la ligne. Minimum absolu — ne pas réduire.
    /// </summary>
    public const float Phone = 88f;

    /// <summary>Colonne à un seul caractère ou à sigle court (« M »/« F ») : l'en-tête fait la largeur.</summary>
    public const float Initial = 36f;

    /// <summary>Montant en francs CFA aligné à droite (« 1 250 000 »).</summary>
    public const float Amount = 72f;
}
