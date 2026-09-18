using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Verrouille un endpoint (ou un contrôleur entier) derrière un module activable par le Directeur :
/// <c>[RequireModule(SchoolModule.Finance)]</c>. Miroir exact de <see cref="RequireFeatureAttribute"/>
/// (même patron de politique paramétrée), mais un axe DIFFÉRENT : ici la formule d'abonnement n'entre
/// pour rien, seul compte le choix opérationnel du Directeur (SchoolSettings.IsXxxEnabled).
///
/// Le nom de politique encode le paramètre (« Module:Finance »), reconstruit à la volée par
/// <see cref="FeaturePolicyProvider"/> — qui résout aussi bien les politiques « Feature:… » que
/// « Module:… », ASP.NET Core n'acceptant qu'un seul <see cref="IAuthorizationPolicyProvider"/> par
/// application.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireModuleAttribute : AuthorizeAttribute
{
    public RequireModuleAttribute(SchoolModule module) => Policy = ModulePolicies.NameFor(module);
}

/// <summary>Convention de nommage des politiques de module, partagée attribut ↔ fournisseur.</summary>
public static class ModulePolicies
{
    public const string Prefix = "Module:";

    public static string NameFor(SchoolModule module) => $"{Prefix}{module}";

    /// <summary>Inverse de <see cref="NameFor"/> : renvoie false si le nom ne suit pas la convention.</summary>
    public static bool TryParse(string policyName, out SchoolModule module)
    {
        module = default;

        return policyName.StartsWith(Prefix, StringComparison.Ordinal)
            && Enum.TryParse(policyName[Prefix.Length..], out module);
    }
}

/// <summary>Exigence d'autorisation portée par <see cref="RequireModuleAttribute"/> — voir <see cref="ModuleAuthorizationHandler"/>.</summary>
public sealed record ModuleRequirement(SchoolModule Module) : IAuthorizationRequirement;
