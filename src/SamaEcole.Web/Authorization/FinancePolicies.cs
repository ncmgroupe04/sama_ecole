namespace SamaEcole.Web.Authorization;

/// <summary>Noms des politiques d'autorisation liées à la délégation Finance (matrice "Photoshop").</summary>
public static class FinancePolicies
{
    /// <summary>
    /// Ajuster un montant de barème déjà défini (PUT /finance/fees/{id}). Voir
    /// CanModifyFeesRequirement pour la règle et CanModifyFeesHandler pour son évaluation.
    /// </summary>
    public const string CanModifyFees = "CanModifyFees";

    /// <summary>
    /// Supprimer (soft delete) une catégorie de frais ou une ligne de barème. Voir
    /// CanDeleteFeesRequirement pour la règle et CanDeleteFeesHandler pour son évaluation.
    /// </summary>
    public const string CanDeleteFees = "CanDeleteFees";
}
