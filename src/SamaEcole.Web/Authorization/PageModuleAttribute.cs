using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Déclare le module dont dépend une PAGE (action de <c>PagesController</c>). Un filtre (<c>PageModuleFilter</c>) le recopie dans
/// <c>ViewData["RequiredModule"]</c> et <c>_Layout</c> émet <c>&lt;meta name="required-module" content="Internat"&gt;</c> ;
/// <c>module-route-guard.js</c> lit cette balise et redirige quand le module est désactivé. Un seul endroit déclare le module
/// d'une page : aucune table route → module à tenir à jour dans le JavaScript.
///
/// CE N'EST PAS UNE BARRIÈRE D'ACCÈS — volontairement pas un <c>AuthorizeAttribute</c>. Les gabarits sont servis sans
/// authentification (le navigateur n'envoie pas d'en-tête <c>Authorization</c> en naviguant) : le serveur ne connaît ni l'école ni
/// le profil au moment du GET de la page. La vraie protection est celle de l'API (<see cref="RequireModuleAttribute"/> → 403
/// <c>MODULE_DISABLED</c>, rôles, RLS). Un utilisateur qui force l'URL obtient un gabarit vide de données.
///
/// Le module déclaré doit être celui du contrôleur API que la page APPELLE : les écrans Halqa / Oustaz / Suivi coranique
/// appellent <c>/api/v1/internat/*</c> (module Internat), pas <c>/api/v1/quran</c> (module Coran).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PageModuleAttribute(SchoolModule module) : Attribute
{
    public SchoolModule Module { get; } = module;
}
