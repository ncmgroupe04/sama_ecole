namespace SamaEcole.Domain.Common;

/// <summary>
/// Année scolaire sénégalaise. Elle commence en OCTOBRE et porte le millésime de sa rentrée :
/// la rentrée d'octobre 2026 ouvre l'année scolaire 2026-2027, étiquetée « 2026 ».
/// C'est ce nombre qui figure dans le matricule (docs/Volume_1_Cahier_des_Charges.md §2.2) :
/// un élève inscrit en septembre 2026 est encore sur l'année 2025, un élève inscrit en
/// octobre 2026 bascule sur l'année 2026.
///
/// Le Sénégal est à UTC+0 toute l'année : raisonner en UTC ne décale donc jamais la bascule.
/// </summary>
public static class AcademicYear
{
    /// <summary>Premier mois de l'année scolaire (octobre).</summary>
    public const int FirstMonth = 10;

    /// <summary>Millésime de l'année scolaire en cours à la date donnée.</summary>
    public static int ForDate(DateTimeOffset date) =>
        date.Month >= FirstMonth ? date.Year : date.Year - 1;
}