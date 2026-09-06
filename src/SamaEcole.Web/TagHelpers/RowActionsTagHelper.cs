using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Menu d'actions de ligne (ligne d'icônes d'action directes).
///
/// Usage :
/// <code>
/// &lt;row-actions&gt;
///     &lt;row-action icon="pencil" label="Modifier" on-click="openEdit(item)" /&gt;
///     &lt;row-action icon="trash-2" label="Supprimer" on-click="openDelete(item)" variant="danger" /&gt;
/// &lt;/row-actions&gt;
/// </code>
/// </summary>
[HtmlTargetElement("row-actions")]
public class RowActionsTagHelper : TagHelper
{
    /// <summary>Expression Alpine x-show optionnelle sur le conteneur entier.</summary>
    public string? Show { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = (await output.GetChildContentAsync()).GetContent();
        var showAttr = string.IsNullOrWhiteSpace(Show) ? "" : $"""x-show="{Show}" x-cloak """;

        output.TagName = null;
        output.Content.SetHtmlContent($$"""
            <div {{showAttr}}class="flex items-center justify-end gap-1">
                {{body}}
            </div>
            """);
    }

    /// <summary>&lt;svg&gt;&lt;use&gt; brut — voir DateFieldTagHelper.Svg pour la raison (le tag
    /// helper &lt;icon&gt; n'est jamais appliqué au HTML injecté après la passe de compilation Razor).</summary>
    internal static string Svg(string name, string cssClass) =>
        $"""<svg aria-hidden="true" viewBox="0 0 24 24" fill="currentColor" class="{cssClass}"><use href="#icon-{WebUtility.HtmlEncode(name)}"></use></svg>""";
}

/// <summary>Une entrée d'action (icône avec tooltip).</summary>
[HtmlTargetElement("row-action", ParentTag = "row-actions")]
public class RowActionTagHelper : TagHelper
{
    /// <summary>Nom du symbole du sprite (ex. « pencil », « trash-2 »).</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Texte visible de l'action (tooltip).</summary>
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
        var buttonClass = isDanger ? "text-red-500 hover:text-red-700 hover:bg-red-50" : "text-slate-500 hover:text-indigo-600 hover:bg-slate-100";
        var showAttr = string.IsNullOrWhiteSpace(Show) ? "" : $"""x-show="{Show}" x-cloak """;
        var label = WebUtility.HtmlEncode(Label);

        output.TagName = null;
        // min-h/min-w 44px sur mobile (plancher tactile — ces boutons vivent dans des tableaux
        // denses au doigt), remis à la taille compacte dès `sm:` pour ne pas gonfler les lignes
        // sur desktop. inline-flex centre l'icône dans la cible élargie.
        output.Content.SetHtmlContent($$"""
            <button type="button" {{showAttr}}x-on:click="{{OnClick}}"
                    title="{{label}}" aria-label="{{label}}"
                    class="inline-flex items-center justify-center min-h-[44px] min-w-[44px] sm:min-h-0 sm:min-w-0 p-1.5 rounded-lg transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-600 {{buttonClass}}">
                {{RowActionsTagHelper.Svg(Icon, "w-[18px] h-[18px] flex-shrink-0")}}
            </button>
            """);
    }
}
