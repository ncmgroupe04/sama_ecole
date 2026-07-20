using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Menu d'actions de ligne (bouton « ⋮ » + carte déroulante icône + libellé, rouge pour l'action
/// destructive) — reproduit le design de référence, en remplacement des boutons icône/pilule
/// dispersés dans chaque ligne (Classes, Matières, Frais, Utilisateurs). Regroupe SANS ajouter
/// d'action : chaque <row-action> ne fait qu'exposer, sous cette présentation commune, un
/// gestionnaire déjà existant côté vue (openEdit, openDelete…) — aucune logique métier nouvelle.
///
/// Usage :
/// <code>
/// &lt;row-actions&gt;
///     &lt;row-action icon="pencil" label="Modifier" on-click="openEdit(item)" /&gt;
///     &lt;row-action icon="trash-2" label="Supprimer" on-click="openDelete(item)" variant="danger" /&gt;
/// &lt;/row-actions&gt;
/// </code>
///
/// Le menu se ferme après un clic sur une action (clic à l'intérieur de la carte, en dehors, ou
/// touche Échap) — même convention de fermeture que <see cref="ModalShellTagHelper"/>.
/// </summary>
[HtmlTargetElement("row-actions")]
public class RowActionsTagHelper : TagHelper
{
    /// <summary>Libellé accessible du déclencheur « ⋮ ».</summary>
    public string Label { get; set; } = "Actions";

    /// <summary>Expression Alpine x-show optionnelle sur le déclencheur entier (ex. « row.fee »
    /// quand aucune action de la liste n'a de sens tant qu'un montant n'existe pas encore).</summary>
    public string? Show { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = (await output.GetChildContentAsync()).GetContent();
        var label = WebUtility.HtmlEncode(Label);
        var showAttr = string.IsNullOrWhiteSpace(Show) ? "" : $"""x-show="{Show}" x-cloak """;

        output.TagName = null;
        output.Content.SetHtmlContent($$"""
            <div {{showAttr}}class="relative inline-block text-left" x-data="{ open: false, openUpward: false }">
                <button type="button"
                        x-on:click="openUpward = $el.getBoundingClientRect().bottom + 200 > window.innerHeight; open = !open"
                        :aria-expanded="open" aria-haspopup="true"
                        title="{{label}}" aria-label="{{label}}"
                        class="p-1.5 text-gray-400 hover:text-gray-700 hover:bg-gray-100 rounded-md focus:outline-none focus:ring-2 focus:ring-primary transition-colors">
                    {{Svg("more-vertical", "w-5 h-5")}}
                </button>
                <div x-show="open" x-cloak x-on:click.outside="open = false" x-on:keydown.escape.window="open = false"
                     x-on:click="open = false"
                     :class="openUpward ? 'bottom-full mb-1 origin-bottom-right' : 'top-full mt-1 origin-top-right'"
                     x-transition:enter="ease-out duration-100" x-transition:enter-start="opacity-0 scale-95" x-transition:enter-end="opacity-100 scale-100"
                     class="absolute right-0 z-20 w-52 rounded-xl bg-white py-1.5 shadow-lg ring-1 ring-gray-100 focus:outline-none">
                    {{body}}
                </div>
            </div>
            """);
    }

    /// <summary>&lt;svg&gt;&lt;use&gt; brut — voir DateFieldTagHelper.Svg pour la raison (le tag
    /// helper &lt;icon&gt; n'est jamais appliqué au HTML injecté après la passe de compilation Razor).</summary>
    internal static string Svg(string name, string cssClass) =>
        $"""<svg aria-hidden="true" viewBox="0 0 24 24" fill="currentColor" class="{cssClass}"><use href="#icon-{WebUtility.HtmlEncode(name)}"></use></svg>""";
}

/// <summary>Une entrée du menu <see cref="RowActionsTagHelper"/> (icône + libellé).</summary>
[HtmlTargetElement("row-action", ParentTag = "row-actions")]
public class RowActionTagHelper : TagHelper
{
    /// <summary>Nom du symbole du sprite (ex. « pencil », « trash-2 »).</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Texte visible de l'action.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Expression Alpine exécutée au clic (ex. « openEdit(item) »).</summary>
    public string OnClick { get; set; } = string.Empty;

    /// <summary>Expression Alpine x-show optionnelle, pour une action visible sous condition seulement.</summary>
    public string? Show { get; set; }

    /// <summary>« default » (gris) ou « danger » (rouge) — pour l'action destructive.</summary>
    public string Variant { get; set; } = "default";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var isDanger = Variant.Equals("danger", StringComparison.OrdinalIgnoreCase);
        var textClass = isDanger ? "text-danger hover:bg-danger-bg" : "text-gray-700 hover:bg-gray-50";
        var iconClass = isDanger ? "text-danger" : "text-gray-400";
        var showAttr = string.IsNullOrWhiteSpace(Show) ? "" : $"""x-show="{Show}" x-cloak """;
        var label = WebUtility.HtmlEncode(Label);

        output.TagName = null;
        output.Content.SetHtmlContent($$"""
            <button type="button" {{showAttr}}x-on:click="{{OnClick}}"
                    class="flex w-full items-center gap-2.5 px-4 py-2.5 text-sm {{textClass}} transition-colors">
                {{RowActionsTagHelper.Svg(Icon, $"w-4 h-4 {iconClass} flex-shrink-0")}}
                <span>{{label}}</span>
            </button>
            """);
    }
}
