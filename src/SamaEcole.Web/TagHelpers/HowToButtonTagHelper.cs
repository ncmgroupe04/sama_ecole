using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Bouton d'aide SPÉCIFIQUE à une fonctionnalité, dans le même esprit que « Comment sont appliqués les
/// coefficients ? » : un bouton jaune bien repérable (<c>.btn-help</c>), posé là où le réglage est délicat,
/// qui ouvre la fenêtre d'aide sur UN article précis du Centre d'aide (wwwroot/js/help.js). Un seul contenu à
/// maintenir : l'article est lu à la demande par le store Alpine <c>howTo</c> (wwwroot/js/how-to.js).
///
/// Usage : <c>&lt;how-to-button article="bareme-frais" label="Comment configurer le barème des frais ?" /&gt;</c>.
/// Les autres attributs (x-show, class…) sont conservés. Un test JS vérifie que chaque <c>article</c> utilisé dans
/// les vues existe bien dans le Centre d'aide (tests/js/how-to.test.mjs).
/// </summary>
[HtmlTargetElement("how-to-button")]
public class HowToButtonTagHelper : TagHelper
{
    /// <summary>Identifiant de l'article du Centre d'aide (ex. « bareme-frais »).</summary>
    public string Article { get; set; } = string.Empty;

    /// <summary>Libellé du bouton, formulé comme la question que se pose l'utilisateur.</summary>
    public string Label { get; set; } = "Comment faire ?";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "button";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("type", "button");
        // x-data vide : crée une portée enfant (accès au store global et aux variables de l'écran hôte).
        output.Attributes.SetAttribute("x-data", "");
        // Article : identifiant de la liste blanche du Centre d'aide, jamais une saisie utilisateur ; encodé quand même.
        output.Attributes.SetAttribute("x-on:click", $"$store.howTo.showArticle('{WebUtility.HtmlEncode(Article)}')");

        var existing = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrWhiteSpace(existing) ? "btn-help" : $"btn-help {existing}");

        output.Content.SetHtmlContent(
            """<svg aria-hidden="true" class="w-4 h-4 shrink-0" fill="currentColor" viewBox="0 0 24 24"><use href="#icon-help-circle"></use></svg>"""
            + $"<span>{WebUtility.HtmlEncode(Label)}</span>");
    }
}
