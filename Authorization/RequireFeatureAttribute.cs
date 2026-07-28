using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Verrouille un endpoint (ou un contrôleur entier) derrière la formule d'abonnement de l'école :
/// <c>[RequireFeature(Feature.SmsNotifications)]</c>.
///
/// Une politique d'autorisation ASP.NET Core est identifiée par une CHAÎNE, pas par un objet — un
/// attribut paramétré passe donc par une politique dont le nom encode le paramètre
/// (« Feature:SmsNotifications »), reconstruite à la volée par <see cref="FeaturePolicyProvider"/>.
/// C'est le patron officiel des politiques paramétrées, et la raison pour laquelle on hérite de
/// <see cref="AuthorizeAttribute"/> plutôt que d'écrire un filtre maison : l'endpoint reste soumis à
/// tout le pipeline d'autorisation habituel (authentification, autres politiques, rôles).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireFeatureAttribute : AuthorizeAttribute
{
    public RequireFeatureAttribute(Feature feature) => Policy = FeaturePolicies.NameFor(feature);
}

/// <summary>Convention de nommage des politiques de fonctionnalité, partagée attribut ↔ fournisseur.</summary>
public static class FeaturePolicies
{
    public const string Prefix = "Feature:";

    public static string NameFor(Feature feature) => $"{Prefix}{feature}";

    /// <summary>Inverse de <see cref="NameFor"/> : renvoie false si le nom ne suit pas la convention.</summary>
    public static bool TryParse(string policyName, out Feature feature)
    {
        feature = default;

        return policyName.StartsWith(Prefix, StringComparison.Ordinal)
            && Enum.TryParse(policyName[Prefix.Length..], out feature);
    }
}
