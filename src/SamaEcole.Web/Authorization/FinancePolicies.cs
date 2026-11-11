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

    /// <summary>Annuler un versement (48 h) : Directeur toujours, Finance sauf retrait du droit par le Directeur (CanCancelPaymentsHandler).</summary>
    public const string CanCancelPayments = "CanCancelPayments";

    /// <summary>Annuler / corriger une fiche de paie : Directeur toujours, Finance sauf retrait du droit par le Directeur (CanCancelPayslipsHandler).</summary>
    public const string CanCancelPayslips = "CanCancelPayslips";

    /// <summary>
    /// Consulter la Situation des mensualités (GET /finance/monthly-status et son export) : Directeur toujours, Finance
    /// seulement si le Directeur l'a ouvert (SchoolSettings.AllowFinanceToViewMonthlyStatus, faux par défaut) —
    /// voir CanViewMonthlyStatusHandler.
    /// </summary>
    public const string CanViewMonthlyStatus = "CanViewMonthlyStatus";
}
