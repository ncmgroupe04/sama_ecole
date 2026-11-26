namespace SamaEcole.Application.Registration;

/// <summary>
/// Version en vigueur des documents juridiques publics (conditions d'utilisation, politique de confidentialité,
/// mentions légales, cookies). Elle est AFFICHÉE sur chaque page et ENREGISTRÉE avec l'acceptation d'une inscription :
/// c'est ce qui permet de prouver, plus tard, quel texte un Directeur avait accepté.
///
/// À CHAQUE modification du fond d'un de ces textes (pas d'une faute de frappe), changer cette valeur (format
/// AAAA-MM-JJ de la mise en vigueur) : les demandes déjà acceptées gardent la version qu'elles ont vue.
/// </summary>
public static class LegalDocuments
{
    public const string CurrentVersion = "2026-11-15";
}
