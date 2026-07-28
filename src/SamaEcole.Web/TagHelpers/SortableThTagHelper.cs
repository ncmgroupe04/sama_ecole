using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// En-tête de colonne triable (Volume 5 §6 : « tri » sur toutes les listes) — un &lt;th&gt; cliquable
/// avec chevron d'état, remplace la cellule d'en-tête statique. Le tri lui-même reste dans le
/// contrôleur Alpine de chaque écran (un <c>computed</c> qui trie le tableau affiché) : ce composant
/// n'est que le déclencheur + l'indicateur visuel, pas la logique de tri.
///
/// Les attributs "sort-key"/"sort-dir"/"on-sort" sont des NOMS d'expressions Alpine, pas des
/// valeurs — même idiome que &lt;pagination on-change="..."&gt; : le composant les recopie tels quels
/// dans x-on:click / x-show, c'est Alpine qui les évalue au moment du clic/rendu.
///
/// Usage :
/// <code>
/// &lt;sort-th column="matricule" sort-key="sortKey" sort-dir="sortDir" on-sort="toggleSort('matricule')"&gt;
///     Matricule
/// &lt;/sort-th&gt;
/// </code>
/// </summary>
[HtmlTargetElement("sort-th")]
public class SortableThTagHelper : TagHelper
{
    /// <summary>Nom du champ trié (ex. "matricule") — comparé à <see cref="SortKey"/> pour savoir si CE en-tête porte le chevron actif.</summary>
    public string Column { get; set; } = string.Empty;

    /// <summary>Nom de la variable Alpine qui contient la colonne actuellement triée (ex. "sortKey").</summary>
    public string SortKey { get; set; } = "sortKey";

    /// <summary>Nom de la variable Alpine qui contient le sens du tri, "asc" ou "desc" (ex. "sortDir").</summary>
    public string SortDir { get; set; } = "sortDir";

    /// <summary>Expression Alpine appelée au clic (ex. "toggleSort('matricule')").</summary>
    public string OnSort { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var label = (await output.GetChildContentAsync()).GetContent();
        var column = WebUtility.HtmlEncode(Column);

        output.TagName = "th";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Fusionne avec un éventuel class="..." déjà posé par l'appelant (ex. alignement text-right)
        // plutôt que de l'écraser.
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var mergedClass = string.IsNullOrWhiteSpace(existingClass)
            ? "cursor-pointer select-none hover:text-gray-700"
            : $"{existingClass} cursor-pointer select-none hover:text-gray-700";
        output.Attributes.SetAttribute("class", mergedClass);
        output.Attributes.SetAttribute("x-on:click", $"{WebUtility.HtmlEncode(OnSort)}");

        // Chevron visible seulement sur la colonne activement triée ; sa rotation (bas/haut) indique
        // le sens. fill="currentColor" (pas de stroke) : convention Fluent UI System Icons du sprite.
        output.Content.SetHtmlContent($$"""
            <span class="inline-flex items-center gap-1">
                <span>{{label}}</span>
                <svg class="h-3.5 w-3.5 shrink-0 transition-transform"
                     viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"
                     x-show="{{SortKey}} === '{{column}}'" x-cloak
                     :class="{{SortDir}} === 'asc' ? 'rotate-180' : ''"><use href="#icon-chevron-down"></use></svg>
            </span>
            """);
    }
}
