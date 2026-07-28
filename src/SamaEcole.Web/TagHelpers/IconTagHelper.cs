using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Icône partagée — référence un &lt;symbol&gt; du sprite rendu une fois par
/// <c>Views/Shared/_IconSprite.cshtml</c> (jeu Fluent UI System Icons, Volume 5 §2.3), plutôt que
/// de recopier un &lt;svg&gt; inline dans chaque vue (le pattern jusqu'ici, source de duplication et
/// de dérive visuelle).
///
/// Les attributs de présentation (fill/…) sont réémis sur CHAQUE instance plutôt que délégués au
/// &lt;svg&gt; caché du sprite : un &lt;symbol&gt; référencé via &lt;use&gt; n'hérite pas de façon
/// fiable des attributs d'un ancêtre display:none selon les navigateurs.
///
/// Contrairement à Lucide/Heroicons (contour au stroke), les icônes Fluent "regular" sont des
/// formes PLEINES : un seul attribut fill="currentColor" suffit, pas de stroke-width/linecap/linejoin.
///
/// Usage : <c>&lt;icon name="users" class="w-5 h-5" /&gt;</c> (décorative, aria-hidden par défaut) ou
/// <c>&lt;icon name="trash-2" class="w-5 h-5" label="Supprimer" /&gt;</c> pour un bouton icône-seule
/// (Volume 5 §8 : aria-label obligatoire sur les contrôles sans texte visible).
/// </summary>
[HtmlTargetElement("icon")]
public class IconTagHelper : TagHelper
{
    /// <summary>Nom du &lt;symbol&gt; dans le sprite (ex. "users" → #icon-users).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Si fourni, l'icône devient porteuse de sens (aria-label) plutôt que décorative.</summary>
    public string? Label { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;

        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        output.Attributes.SetAttribute("fill", "currentColor");

        if (string.IsNullOrWhiteSpace(Label))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", WebUtility.HtmlEncode(Label));
        }

        output.Content.SetHtmlContent($"""<use href="#icon-{WebUtility.HtmlEncode(Name)}"></use>""");
    }
}
