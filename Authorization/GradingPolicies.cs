namespace SamaEcole.Web.Authorization;

/// <summary>Noms des politiques d'autorisation liées à la délégation de la notation (ticket JGK-G02).</summary>
public static class GradingPolicies
{
    /// <summary>
    /// Barème, matières/coefficients, mentions du bulletin. Voir CanManageGradingScaleRequirement
    /// pour la règle et CanManageGradingScaleHandler pour son évaluation.
    /// </summary>
    public const string CanManageGradingScale = "CanManageGradingScale";
}
