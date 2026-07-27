using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Construit à la volée les politiques « Feature:… » posées par <see cref="RequireFeatureAttribute"/>.
/// Sans ce fournisseur, ASP.NET Core chercherait ces noms dans les politiques enregistrées au
/// démarrage (Program.cs) et lèverait « The AuthorizationPolicy named ... was not found » : il y en a
/// une par valeur de l'énumération, les déclarer une à une serait redondant et à re-synchroniser à
/// chaque ajout.
///
/// DÉLÈGUE tout le reste (CanModifyFees, CanAccessSchoolResource, politique par défaut, politique de
/// repli) au fournisseur par défaut : ce fournisseur-ci ne connaît QUE son propre préfixe et ne doit
/// pas devenir un passage obligé pour les politiques déclarées normalement.
/// </summary>
public class FeaturePolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!FeaturePolicies.TryParse(policyName, out var feature))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var policy = new AuthorizationPolicyBuilder()
            // Exiger l'authentification EN PLUS de la fonctionnalité : sans cela, un appel anonyme
            // atteindrait le handler sans identité, donc sans école — et se ferait refuser pour la
            // mauvaise raison (« formule insuffisante » au lieu de « non authentifié »).
            .RequireAuthenticatedUser()
            .AddRequirements(new FeatureRequirement(feature))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
