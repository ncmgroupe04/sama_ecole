using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Sélecteur de date partagé, reproduisant le calendrier de référence (carte blanche arrondie,
/// en-tête mois/année avec chevrons, semaine Lu→Di, jour sélectionné en pastille pleine, bouton
/// « Terminé ») — un <c>&lt;input type="date"&gt;</c> natif ne peut pas être restylé pour
/// correspondre : son calendrier est dessiné par le navigateur/l'OS, hors de portée du CSS.
///
/// Remplace TOUS les <c>&lt;input type="date"&gt;</c> de l'application (Volume 5 : « composants
/// réutilisables développés une seule fois », pas redupliqués par écran).
///
/// <see cref="Model"/> est une EXPRESSION Alpine brute (ex. « form.birthDate », «
/// editingStudent.birthDate »), jamais une donnée utilisateur : émise telle quelle, exactement
/// comme <see cref="ModalShellTagHelper.Open"/>. La logique de navigation mois/année vit dans le
/// composant Alpine partagé <c>dateField()</c> (wwwroot/js/ui-components.js), chargé une fois par
/// _Layout.cshtml — seule la valeur sélectionnée (I/O) passe par <see cref="Model"/>.
///
/// Un &lt;input type="date"&gt; natif reste présent, visuellement masqué (opacity-0, jamais
/// display:none — un champ non rendu est exclu de la validation de contrainte HTML, voir
/// spec §4.10.20.1) : il porte l'attribut <c>required</c> et reste le déclencheur de la validation
/// native du formulaire, synchronisé par le même x-model que le calendrier visible.
/// </summary>
[HtmlTargetElement("date-field")]
public class DateFieldTagHelper : TagHelper
{
    /// <summary>Expression Alpine liée (ex. « newStudent.birthDate »). Lue et écrite telle quelle.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Id HTML optionnel, pour un &lt;label for="..."&gt; externe.</summary>
    public string? Id { get; set; }

    /// <summary>Si vrai, le champ caché porte <c>required</c> (validation native du formulaire).</summary>
    public bool Required { get; set; }

    /// <summary>Texte affiché quand aucune date n'est encore choisie.</summary>
    public string Placeholder { get; set; } = "Sélectionner une date";

    /// <summary>Libellé accessible du déclencheur ; par défaut, reprend <see cref="Placeholder"/>.</summary>
    public string? AriaLabel { get; set; }

    /// <summary>Classes Tailwind additionnelles pour le déclencheur (ex. « mt-0 » dans une barre de filtres).</summary>
    public string? Class { get; set; }

    private static readonly string[] MonthNames =
    [
        "Janvier", "Février", "Mars", "Avril", "Mai", "Juin",
        "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"
    ];

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        var idAttr = string.IsNullOrWhiteSpace(Id) ? "" : $""" id="{WebUtility.HtmlEncode(Id)}" """.Trim() + " ";
        var requiredAttr = Required ? "required " : "";
        var placeholder = WebUtility.HtmlEncode(Placeholder);
        var ariaLabel = WebUtility.HtmlEncode(AriaLabel ?? Placeholder);
        var extraClass = string.IsNullOrWhiteSpace(Class) ? "" : " " + Class;

        // Options de mois/année rendues en HTML STATIQUE (pas via x-for) : un <select x-model> dont
        // les <option> arrivent après coup, le temps qu'Alpine peuple un <template x-for>, se retrouve
        // sans option correspondante au tout premier rendu — le navigateur retombe alors sur la
        // première option de la liste (janvier ; l'année la plus haute, la liste étant décroissante),
        // qu'Alpine ne corrige jamais ensuite puisque viewMonth/viewYear, eux, n'ont pas changé. Des
        // options déjà présentes dans le HTML initial évitent la course : x-model trouve tout de suite
        // la bonne <option>. Plage d'années : 100 ans en arrière (date de naissance) et 5 en avant.
        var monthOptions = string.Concat(MonthNames.Select((name, index) =>
            $"""<option value="{index}">{WebUtility.HtmlEncode(name)}</option>"""));

        var currentYear = DateTime.Now.Year;
        var yearOptions = string.Concat(
            Enumerable.Range(currentYear - 100, 106).Reverse()
                .Select(year => $"""<option value="{year}">{year}</option>"""));

        output.Content.SetHtmlContent($$"""
            <div class="relative" x-data="dateField()">
                <input type="date" {{requiredAttr}}x-model="{{Model}}" tabindex="-1" aria-hidden="true"
                       class="absolute left-0 top-0 h-px w-px opacity-0 pointer-events-none -z-10" />
                <button type="button" {{idAttr}}x-on:click="toggle({{Model}})" :aria-expanded="open" aria-haspopup="dialog"
                        aria-label="{{ariaLabel}}"
                        class="input-field{{extraClass}} w-full flex items-center justify-between gap-2 bg-white text-left cursor-pointer">
                    <span :class="{{Model}} ? 'text-gray-900' : 'text-gray-400'" x-text="{{Model}} ? formatDisplay({{Model}}) : '{{placeholder}}'"></span>
                    {{Svg("calendar", "w-4 h-4 text-gray-400 flex-shrink-0")}}
                </button>

                <div x-show="open" x-cloak x-on:click.outside="open = false" x-on:keydown.escape="open = false"
                     x-transition:enter="ease-out duration-150" x-transition:enter-start="opacity-0 scale-95" x-transition:enter-end="opacity-100 scale-100"
                     class="absolute z-30 mt-2 w-[300px] rounded-2xl bg-white p-4 shadow-xl ring-1 ring-gray-100">
                    <div class="flex items-center justify-between gap-1 pb-3 mb-2 border-b border-gray-100">
                        <button type="button" x-on:click="prevMonth()" aria-label="Mois précédent"
                                class="p-1.5 rounded-full text-gray-400 hover:bg-gray-50 hover:text-gray-700 transition-colors shrink-0">
                            {{Svg("chevron-left", "w-5 h-5")}}
                        </button>
                        <!-- Navigation rapide : sélection directe du mois/année plutôt que de défiler
                             chevron par chevron jusqu'à une date lointaine (ex. une date de naissance). -->
                        <div class="flex items-center gap-1 min-w-0">
                            <select x-model.number="viewMonth" aria-label="Mois"
                                    class="text-sm font-semibold text-gray-900 bg-transparent border-0 rounded-md py-1 pl-1.5 pr-6 cursor-pointer hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-primary">
                                {{monthOptions}}
                            </select>
                            <select x-model.number="viewYear" aria-label="Année"
                                    class="text-sm font-semibold text-gray-900 bg-transparent border-0 rounded-md py-1 pl-1.5 pr-6 cursor-pointer hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-primary">
                                {{yearOptions}}
                            </select>
                        </div>
                        <button type="button" x-on:click="nextMonth()" aria-label="Mois suivant"
                                class="p-1.5 rounded-full text-gray-400 hover:bg-gray-50 hover:text-gray-700 transition-colors shrink-0">
                            {{Svg("chevron-right", "w-5 h-5")}}
                        </button>
                    </div>

                    <div class="grid grid-cols-7 text-center text-xs font-medium text-gray-400 mb-1">
                        <template x-for="wd in weekdayLabels" :key="wd">
                            <span x-text="wd"></span>
                        </template>
                    </div>

                    <div class="grid grid-cols-7 gap-y-1 text-center text-sm">
                        <template x-for="day in days" :key="day.iso">
                            <button type="button" :disabled="!day.currentMonth"
                                    x-on:click="day.currentMonth && ({{Model}} = day.iso, open = false)"
                                    :class="{
                                        'text-gray-300 cursor-default': !day.currentMonth,
                                        'text-gray-900 hover:bg-gray-50': day.currentMonth && {{Model}} !== day.iso,
                                        'bg-primary-600 text-white font-semibold hover:bg-primary-600': {{Model}} === day.iso,
                                        'ring-1 ring-inset ring-primary-600': isToday(day.iso) && {{Model}} !== day.iso
                                    }"
                                    class="mx-auto flex h-9 w-9 items-center justify-center rounded-lg transition-colors"
                                    x-text="day.label"></button>
                        </template>
                    </div>

                    <div class="mt-3 pt-3 border-t border-gray-100 flex justify-end">
                        <button type="button" x-on:click="open = false" class="btn-primary px-4 py-2 text-sm">Terminé</button>
                    </div>
                </div>
            </div>
            """);
    }

    /// <summary>
    /// Icône rendue en <c>&lt;svg&gt;&lt;use&gt;</c> brut, PAS via le tag helper <c>&lt;icon&gt;</c> :
    /// ce HTML est injecté après coup par <see cref="TagHelperContent.SetHtmlContent"/>, hors de la
    /// passe de compilation Razor qui transforme normalement les tag helpers — un &lt;icon&gt; écrit
    /// ici resterait tel quel, jamais converti en &lt;svg&gt; (même contrainte que
    /// <see cref="ModalShellTagHelper"/>, qui écrit déjà son propre &lt;svg&gt;/&lt;use&gt; pour la
    /// même raison).
    /// </summary>
    private static string Svg(string name, string cssClass) =>
        $"""<svg aria-hidden="true" viewBox="0 0 24 24" fill="currentColor" class="{cssClass}"><use href="#icon-{name}"></use></svg>""";
}
