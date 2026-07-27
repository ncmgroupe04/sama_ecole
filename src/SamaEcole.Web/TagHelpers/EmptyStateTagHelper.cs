using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// État vide partagé (Phase 1 de la refonte UI/UX) — pastille d'icône + titre + indication, le bloc
/// « Aucune classe n'est configurée / Créez votre première… » recopié à l'identique dans chaque écran
/// de liste. Émet uniquement le bloc centré : l'appelant décide de son enveloppe (cellule de tableau
/// avec <c>colspan</c>, ou &lt;div&gt;) et de sa condition d'affichage (<c>x-show</c>).
///
/// Usage :
/// <code>
/// &lt;empty-state icon="layers" title="Aucune classe n'est configurée."
///              hint="Créez votre première classe pour commencer les inscriptions." /&gt;
/// </code>
/// </summary>
[HtmlTargetElement("empty-state")]
public class EmptyStateTagHelper : TagHelper
{
    /// <summary>Nom d'icône du sprite (ex. "layers" → #icon-layers).</summary>
    public string Icon { get; set; } = "alert-circle";

    /// <summary>Message principal (ex. « Aucune classe n'est configurée. »).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Indication secondaire optionnelle (ex. « Créez votre première classe… »).</summary>
    public string? Hint { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        var hint = string.IsNullOrWhiteSpace(Hint)
            ? ""
            : $"""<p class="mt-1 text-xs text-slate-400">{WebUtility.HtmlEncode(Hint)}</p>""";

        output.Content.SetHtmlContent($"""
            <div class="flex flex-col items-center justify-center py-12 text-center">
                <div class="mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-indigo-50 text-indigo-500">
                    <svg class="h-6 w-6" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><use href="#icon-{WebUtility.HtmlEncode(Icon)}"></use></svg>
                </div>
                <p class="font-medium text-slate-500">{WebUtility.HtmlEncode(Title)}</p>
                {hint}
            </div>
            """);
    }
}
