using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Liste déroulante avec recherche interne, reproduisant le composant de référence (bouton bordé
/// arrondi, bordure primary à l'ouverture, chevron qui bascule, menu carte avec champ de recherche
/// et défilement personnalisé) — un &lt;select&gt; natif ne peut pas être restylé à ce niveau : son
/// menu est dessiné par le navigateur/l'OS, hors de portée du CSS (même contrainte que
/// <see cref="DateFieldTagHelper"/> pour le calendrier).
///
/// <see cref="Model"/> est une EXPRESSION Alpine brute (ex. « form.country »), jamais une donnée
/// utilisateur : émise telle quelle, comme <see cref="DateFieldTagHelper.Model"/>. La logique de
/// recherche/filtrage vit dans le composant Alpine partagé <c>selectField()</c>
/// (wwwroot/js/ui-components.js) ; seules la valeur sélectionnée (I/O) et la liste d'options
/// passent par les attributs de ce Tag Helper.
///
/// Un &lt;input type="text"&gt; natif reste présent, visuellement masqué (opacity-0, jamais
/// display:none — un champ non rendu est exclu de la validation de contrainte HTML), il porte
/// l'attribut <c>required</c> et reste le déclencheur de la validation native du formulaire.
/// </summary>
[HtmlTargetElement("select-field")]
public class SelectFieldTagHelper : TagHelper
{
    /// <summary>Expression Alpine liée (ex. « newSchool.country »). Lue et écrite telle quelle.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Options proposées (valeur soumise + libellé affiché/recherché).</summary>
    public IEnumerable<SelectFieldOption> Options { get; set; } = [];

    /// <summary>Id HTML optionnel, pour un &lt;label for="..."&gt; externe.</summary>
    public string? Id { get; set; }

    /// <summary>Si vrai, le champ caché porte <c>required</c> (validation native du formulaire).</summary>
    public bool Required { get; set; }

    /// <summary>Texte affiché quand aucune option n'est encore choisie.</summary>
    public string Placeholder { get; set; } = "Sélectionner une option";

    /// <summary>Texte indicatif du champ de recherche interne.</summary>
    public string SearchPlaceholder { get; set; } = "Rechercher...";

    /// <summary>Libellé accessible du déclencheur ; par défaut, reprend <see cref="Placeholder"/>.</summary>
    public string? AriaLabel { get; set; }

    /// <summary>Classes Tailwind additionnelles pour le déclencheur.</summary>
    public string? Class { get; set; }

    private static readonly JsonSerializerOptions OptionsJsonSettings = new(JsonSerializerDefaults.Web);

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        var idAttr = string.IsNullOrWhiteSpace(Id) ? "" : $""" id="{WebUtility.HtmlEncode(Id)}" """.Trim() + " ";
        var requiredAttr = Required ? "required " : "";
        var placeholder = WebUtility.HtmlEncode(Placeholder);
        var searchPlaceholder = WebUtility.HtmlEncode(SearchPlaceholder);
        var ariaLabel = WebUtility.HtmlEncode(AriaLabel ?? Placeholder);
        var extraClass = string.IsNullOrWhiteSpace(Class) ? "" : " " + Class;

        // Sérialisé en JSON (littéral JS valide) puis HTML-encodé pour s'insérer sans risque dans un
        // attribut x-data délimité par des guillemets doubles — même règle que Model/Open ailleurs :
        // ce n'est jamais de la donnée utilisateur non filtrée, mais des options fournies par la vue.
        var optionsJson = WebUtility.HtmlEncode(JsonSerializer.Serialize(Options, OptionsJsonSettings));

        output.Content.SetHtmlContent($$"""
            <div class="relative" x-data="selectField({{optionsJson}})">
                <input type="text" {{requiredAttr}}x-model="{{Model}}" tabindex="-1" aria-hidden="true"
                       class="absolute left-0 top-0 h-px w-px opacity-0 pointer-events-none -z-10" />
                <button type="button" {{idAttr}}x-on:click="toggle()" :aria-expanded="open" aria-haspopup="listbox"
                        aria-label="{{ariaLabel}}"
                        :class="open ? 'border-primary' : 'border-gray-300'"
                        class="mt-1{{extraClass}} flex w-full items-center justify-between gap-2 rounded-md border bg-white p-2 text-left shadow-sm transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 sm:text-sm">
                    <span :class="{{Model}} ? 'text-gray-900' : 'text-gray-400'" x-text="{{Model}} ? labelFor({{Model}}) : '{{placeholder}}'"></span>
                    {{Svg("chevron-down", "w-4 h-4 text-gray-400 flex-shrink-0 transition-transform duration-150")}}
                </button>

                <div x-show="open" x-cloak x-on:click.outside="close()" x-on:keydown.escape="close()"
                     x-transition:enter="ease-out duration-150" x-transition:enter-start="opacity-0 scale-95" x-transition:enter-end="opacity-100 scale-100"
                     class="absolute z-30 mt-2 w-full rounded-lg bg-white p-2 shadow-lg ring-1 ring-gray-100">
                    <div class="relative mb-2">
                        <span aria-hidden="true" class="absolute inset-y-0 left-0 flex items-center pl-3">
                            {{Svg("search", "w-4 h-4 text-gray-300")}}
                        </span>
                        <input type="text" x-ref="search" x-model="search" placeholder="{{searchPlaceholder}}"
                               class="w-full rounded-md border border-gray-200 py-1.5 pl-9 pr-2 text-sm text-gray-700 placeholder:text-gray-400 focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary" />
                    </div>

                    <ul role="listbox" class="select-field-list max-h-56 space-y-0.5 overflow-y-auto pr-1">
                        <template x-for="option in filteredOptions" :key="option.value">
                            <li role="option" :aria-selected="({{Model}} === option.value).toString()">
                                <button type="button" x-on:click="{{Model}} = option.value; close()"
                                        :class="{{Model}} === option.value ? 'bg-primary-50 text-primary-700' : 'text-gray-700 hover:bg-gray-50'"
                                        class="w-full rounded-md px-3 py-2 text-left text-sm transition-colors" x-text="option.label"></button>
                            </li>
                        </template>
                        <li x-show="filteredOptions.length === 0" x-cloak class="px-3 py-2 text-sm text-gray-400">Aucun résultat</li>
                    </ul>
                </div>
            </div>
            """);
    }

    /// <summary>Voir <see cref="DateFieldTagHelper.Svg"/> : icône injectée hors passe Razor, donc en &lt;svg&gt; brut.</summary>
    private static string Svg(string name, string cssClass) =>
        $"""<svg aria-hidden="true" viewBox="0 0 24 24" fill="currentColor" class="{cssClass}"><use href="#icon-{name}"></use></svg>""";
}

/// <summary>Option d'une <c>&lt;select-field&gt;</c> : valeur soumise et libellé affiché/recherché.</summary>
public sealed record SelectFieldOption(string Value, string Label);
