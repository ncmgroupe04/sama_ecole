using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Barre de pagination partagée (Phase 1 de la refonte UI/UX), généralisée depuis le prev/next bricolé
/// de l'écran Élèves — le seul écran qui paginait, alors que Classes/Matières/Frais chargeaient des
/// listes entières. Les autres écrans peuvent désormais paginer avec le même composant.
///
/// Les attributs sont des NOMS d'expressions Alpine côté client (comme <c>open</c> l'est pour
/// modal-shell) : ce composant ne connaît pas les valeurs, il tisse juste les liaisons. L'état
/// (page/pageSize/total) et le rechargement (<see cref="OnChange"/>) vivent dans le contrôleur Alpine
/// de l'écran.
///
/// Usage : <c>&lt;pagination page="page" page-size="pageSize" total="totalCount" on-change="loadStudents()" /&gt;</c>
/// </summary>
[HtmlTargetElement("pagination")]
public class PaginationTagHelper : TagHelper
{
    /// <summary>Nom de la variable Alpine portant la page courante (1-based). Défaut : "page".</summary>
    public string Page { get; set; } = "page";

    /// <summary>Nom de la variable Alpine portant la taille de page. Défaut : "pageSize".</summary>
    public string PageSize { get; set; } = "pageSize";

    /// <summary>Nom de la variable Alpine portant le total de résultats. Défaut : "totalCount".</summary>
    public string Total { get; set; } = "totalCount";

    /// <summary>Expression Alpine à exécuter après changement de page (rechargement). Ex. "loadStudents()".</summary>
    public string OnChange { get; set; } = string.Empty;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        // Bornes de l'intervalle affiché (« Affichage de X à Y sur Z »).
        var from = $"{Total} > 0 ? ({Page} - 1) * {PageSize} + 1 : 0";
        var to = $"Math.min({Page} * {PageSize}, {Total})";

        output.Content.SetHtmlContent($$"""
            <div class="flex flex-col items-center justify-between gap-4 border-t border-gray-200 p-4 sm:flex-row">
                <div class="text-sm text-gray-500">
                    Affichage de <span class="font-medium text-gray-900" x-text="{{from}}"></span>
                    à <span class="font-medium text-gray-900" x-text="{{to}}"></span>
                    sur <span class="font-medium text-gray-900" x-text="{{Total}}"></span> résultats
                </div>
                <nav class="inline-flex rounded-md shadow-sm">
                    <button type="button"
                            x-on:click="if ({{Page}} > 1) { {{Page}}--; {{OnChange}} }"
                            :disabled="{{Page}} === 1"
                            class="relative inline-flex items-center rounded-l-md border border-gray-300 bg-white px-2 py-2 text-sm font-medium text-gray-500 transition-colors hover:bg-gray-50 disabled:opacity-50">
                        <span class="sr-only">Précédent</span>
                        <svg class="h-5 w-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><use href="#icon-chevron-left"></use></svg>
                    </button>
                    <span class="relative z-10 inline-flex items-center border border-primary bg-primary-50 px-4 py-2 text-sm font-medium text-primary-700" x-text="{{Page}}"></span>
                    <button type="button"
                            x-on:click="if ({{Page}} * {{PageSize}} < {{Total}}) { {{Page}}++; {{OnChange}} }"
                            :disabled="{{Page}} * {{PageSize}} >= {{Total}}"
                            class="relative inline-flex items-center rounded-r-md border border-gray-300 bg-white px-2 py-2 text-sm font-medium text-gray-500 transition-colors hover:bg-gray-50 disabled:opacity-50">
                        <span class="sr-only">Suivant</span>
                        <svg class="h-5 w-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><use href="#icon-chevron-right"></use></svg>
                    </button>
                </nav>
            </div>
            """);
    }
}
