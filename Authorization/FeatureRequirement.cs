using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Exige que la formule d'abonnement de l'école courante inclue <see cref="Feature"/>. Posée par
/// <see cref="RequireFeatureAttribute"/> via <see cref="FeaturePolicyProvider"/>, évaluée par
/// FeatureAuthorizationHandler.
/// </summary>
public class FeatureRequirement(Feature feature) : IAuthorizationRequirement
{
    public Feature Feature { get; } = feature;
}
