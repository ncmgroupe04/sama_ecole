using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Construit à la volée les politiques paramétrées de ce produit : « Feature:… » (posées par
/// <see cref="RequireFeatureAttribute"/>, formule d'abonnement) ET « Module:… » (posées par
/// <see cref="RequireModuleAttribute"/>, choix opérationnel du Directeur). Sans ce fournisseur,
/// ASP.NET Core chercherait ces noms dans les politiques enregistrées au démarrage (Program.cs) et
/// lèverait « The AuthorizationPolicy named ... was not found » : il y en a une par valeur de chaque
/// énumération, les déclarer une à une serait redondant et à re-synchroniser à chaque ajout.
///
/// Les deux préfixes partagent ce même fournisseur — et non un fournisseur chacun — parce
/// qu'ASP.NET Core n'accepte qu'un seul <see cref="IAuthorizationPolicyProvider"/> par application ;
/// le second enregistré remplacerait silencieusement le premier. Le nom de la classe reste
/// « Feature… » pour l'historique (règle #Feature était le premier axe paramétré du produit), mais
/// elle résout maintenant les deux.
///
/// DÉLÈGUE tout le reste (CanModifyFees, CanAccessSchoolResource, politique par défaut, politique de
/// repli) au fournisseur par défaut : ce fournisseur-ci ne connaît QUE ses deux préfixes et ne doit
/// pas devenir un passage obligé pour les politiques déclarées normalement.
/// </summary>
public class FeaturePolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Exiger l'authentification EN PLUS de la fonctionnalité/du module : sans cela, un appel
        // anonyme atteindrait le handler sans identité, donc sans école — et se ferait refuser pour
        // la mauvaise raison (« formule insuffisante »/« module désactivé » au lieu de « non
        // authentifié »).
        if (FeaturePolicies.TryParse(policyName, out var feature))
        {
            var featurePolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new FeatureRequirement(feature))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(featurePolicy);
        }

        if (ModulePolicies.TryParse(policyName, out var module))
        {
            var modulePolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new ModuleRequirement(module))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(modulePolicy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
