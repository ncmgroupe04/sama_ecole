using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Panneau de formulaire partagé (« Ajouter une classe », « Ajouter un élève »…) : centré avec fond
/// assombri sur ordinateur/tablette, plein écran sur mobile où une petite boîte serait trop exiguë
/// (Volume 5 §2.4 — points de rupture responsive, seuil `sm:` de Tailwind ≈ 640px). Une seule
/// implémentation, partagée par tous les écrans (Volume 5 §2 et §7 : « composants réutilisables
/// développés une seule fois »), plutôt que redupliquée dans chaque vue comme c'était le cas jusqu'ici
/// (panneau latéral copié-collé dans Students/Classrooms/Subjects/Fees/_SchoolYearsPanel).
///
/// Fermeture par le fond, le bouton ✕, ou Échap — même convention que les dialogues déjà centrés du
/// module Finance (Volume 5 §5 : « navigation clavier complète »).
///
/// Usage :
/// <code>
/// &lt;modal-shell open="isCreateOpen" title="Ajouter une classe"&gt;
///     &lt;modal-subtitle&gt;Texte fixe ou liaison Alpine (x-text) libre.&lt;/modal-subtitle&gt;
///     ... corps du formulaire, inchangé ...
/// &lt;/modal-shell&gt;
/// </code>
///
/// <see cref="Open"/> et <see cref="OnClose"/> sont des EXPRESSIONS Alpine.js (ex. « isCreateOpen »,
/// « detailStudent »), jamais des données utilisateur : elles sont émises telles quelles, exactement
/// comme tout attribut x-show/x-on écrit directement dans une vue Razor par le développeur.
/// </summary>
[HtmlTargetElement("modal-shell")]
public class ModalShellTagHelper : TagHelper
{
    /// <summary>Expression Alpine de visibilité (booléen, ou objet dont la troncature pilote l'affichage).</summary>
    public string Open { get; set; } = "false";

    /// <summary>
    /// Titre affiché dans l'en-tête violet. Texte simple, encodé automatiquement. Pour un titre
    /// DYNAMIQUE (liaison Alpine), utiliser plutôt un enfant &lt;modal-title&gt; qui prend le dessus.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Largeur maximale sur écran ≥ sm : md | lg | xl | 2xl | 60 | wide | 3xl (défaut). Les formulaires
    /// courts (montant, confirmation) respirent mieux en md/lg ; le défaut 3xl préserve les usages
    /// existants. <c>60</c> et <c>wide</c> sont des pourcentages de la largeur d'écran (60 % / 85 %),
    /// pour un formulaire à plusieurs colonnes qui reste à l'étroit dans les gabarits fixes.
    /// </summary>
    public string Size { get; set; } = "3xl";

    /// <summary>
    /// Expression Alpine liée en <c>:style</c> sur le panneau, pour piloter dynamiquement sa largeur
    /// depuis le composant hôte — ex. l'aperçu PDF qui ajuste sa largeur au format du document
    /// (A4 portrait / paysage / A5) plutôt qu'à un gabarit fixe. S'ajoute à <see cref="Size"/> (qui
    /// reste le plafond de repli tant que l'expression renvoie une chaîne vide).
    /// </summary>
    public string? PanelStyle { get; set; }

    /// <summary>
    /// Masquer l'en-tête bleu par défaut pour créer un en-tête personnalisé dans le corps de la modale.
    /// </summary>
    public bool HideHeader { get; set; } = false;

    /// <summary>
    /// Supprimer les espacements internes (padding) du corps de la modale pour un rendu bord à bord.
    /// </summary>
    public bool NoPadding { get; set; } = false;

    /// <summary>
    /// Hauteur FIXE et haute (≈ 92 vh) au lieu de « s'adapte au contenu, plafonné à 90 vh ». Pour une
    /// modale dont le corps doit remplir l'écran — l'aperçu PDF (l'iframe occupe alors toute la place).
    /// </summary>
    public bool Tall { get; set; } = false;

    /// <summary>
    /// Expression Alpine exécutée à la fermeture (fond, ✕, Échap). Par défaut « {Open} = false » ; à
    /// fournir explicitement quand <see cref="Open"/> n'est pas un booléen simple — ex. la fiche élève
    /// se ferme par « detailStudent = null », pas par une affectation à false.
    /// </summary>
    public string? OnClose { get; set; }

    /// <summary>
    /// Vrai (défaut) : la modale se ferme aussi au clic sur le fond assombri et à la touche Échap —
    /// la convention pour la quasi-totalité des panneaux. À passer à <c>false</c> pour une modale où
    /// une fermeture accidentelle ferait perdre une saisie ou un contexte de travail (ex. le guichet
    /// rapide de la caisse) : seuls le bouton ✕ et un bouton explicite du corps ferment alors. La
    /// fermeture programmatique (<c>close-modals</c>) reste active dans les deux cas.
    /// </summary>
    public bool Dismissible { get; set; } = true;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // GetChildContentAsync exécute aussi les <modal-subtitle>/<modal-title> imbriqués, qui écrivent
        // dans context.Items (partagé par référence tout au long de l'arborescence) avant que ce parent
        // ne le relise juste après — le patron documenté de communication enfant → ancêtre des Tag Helpers.
        var body = (await output.GetChildContentAsync()).GetContent();
        var subtitle = context.Items.TryGetValue(ModalSubtitleTagHelper.ItemsKey, out var value) ? (string)value! : null;
        var titleSlot = context.Items.TryGetValue(ModalTitleTagHelper.ItemsKey, out var t) ? (string)t! : null;
        var footer = context.Items.TryGetValue(ModalFooterTagHelper.ItemsKey, out var f) ? (string)f! : null;
        var close = string.IsNullOrWhiteSpace(OnClose) ? $"{Open} = false" : OnClose;

        // Fermeture "accidentelle" (fond + Échap) : retirée quand Dismissible est faux. Le ✕ de
        // l'en-tête et l'événement close-modals restent, eux, toujours câblés.
        var escapeClose = Dismissible ? $""" x-on:keydown.escape.window="{close}" """ : " ";
        var backdropClose = Dismissible ? $""" x-on:click="{close}" """ : "";

        // Un <modal-title> (HTML brut, liaisons Alpine possibles) l'emporte sur l'attribut title encodé.
        var titleHtml = titleSlot ?? WebUtility.HtmlEncode(Title);
        var maxWidth = MaxWidthClass(Size);
        // Liaison Alpine émise telle quelle (comme Open/OnClose) : jamais une donnée utilisateur.
        var panelStyleAttr = string.IsNullOrWhiteSpace(PanelStyle) ? "" : $""" x-bind:style="{PanelStyle}" """;

        // En-tête rendu SEULEMENT s'il porte un contenu réel — un titre OU un sous-titre. Sans ça,
        // une modale sans titre (guidage, confirmation nue) affichait une bande blanche vide d'une
        // centaine de pixels, là juste pour loger la croix. HideHeader=true reste un cas distinct :
        // la vue fournit alors son propre en-tête dans le corps (fiche élève…), on n'y touche pas.
        var hasHeader = !HideHeader && (!string.IsNullOrWhiteSpace(titleHtml) || subtitle is not null);
        var headerlessDialog = !HideHeader && !hasHeader;

        // Croix repositionnée DANS le coin du corps quand l'en-tête disparaît : discrète (pas le
        // gros carré rose de l'en-tête), mais toujours présente — une modale garde une sortie au clic.
        var cornerClose = headerlessDialog
            ? $"""
                <button type="button" x-on:click="{close}" aria-label="Fermer"
                        class="absolute right-3 top-3 z-10 flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600 focus:outline-none focus:ring-2 focus:ring-primary">
                    <svg aria-hidden="true" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"></path></svg>
                </button>
                """
            : "";

        // Corps : fond teinté + space-y-6 pour un FORMULAIRE ; pour un dialogue compact sans en-tête
        // (guidage, confirmation) on retire la teinte et l'inter-espace — confirm-dialog-body porte
        // déjà son propre rythme vertical, resserré.
        var bodyClasses = NoPadding ? "" : headerlessDialog ? "p-6" : "p-6 space-y-6 bg-slate-50/30";

        output.TagName = null; // pas de <modal-shell> littéral au rendu : uniquement le HTML ci-dessous.

        // ──────────────────────────────────────────────────────────────────────────────────────────
        // x-teleport="body" : la modale est RENDUE COMME ENFANT DIRECT DE <body>, où qu'elle soit
        // déclarée. Sans ça, une modale déclarée dans un ANCÊTRE qui forme un contexte d'empilement
        // reste plafonnée au z-index de cet ancêtre. Cas concret : l'assistant de démarrage est dans
        // <header>, or <header> est un flex-item porteur de `z-10` — donc un contexte d'empilement à
        // z-10. Le `z-50` de la modale ne comptait alors QUE face aux enfants de <header> ; dans la
        // page, toute la modale restait à z-10, SOUS le sous-header sticky (z-20) et la barre latérale
        // (z-30), qui passaient DEVANT le fond assombri (bug « chevauchement des overlays »).
        // Alpine conserve la portée du composant d'origine à travers le teleport : `{Open}`, `{close}`
        // et les liaisons de {body} (x-model, etc.) restent évaluées dans le scope de la vue hôte.
        //
        // Barème z-index unifié (voir aussi Styles/input.css) :
        //   header / sous-header sticky ........ z-10 / z-20   ·   menus déroulants, popovers .. z-30
        //   tiroir latéral mobile + overlay ... z-30 / z-20    ·   MODALE (overlay + panneau) .. z-50
        //   toasts / notifications ............ z-[60]
        // Le fond assombri est enfant `fixed` sans z propre → se peint sous le panneau `relative` par
        // l'ordre du DOM. `bg-slate-900/60 backdrop-blur-sm` : opaque ET flouté (un `bg-opacity-75`
        // laissait transparaître champs et en-têtes). `x-effect` fige le défilement de la page
        // derrière la modale (window.__modalScrollLock, wwwroot/js/ui-components.js).
        // ──────────────────────────────────────────────────────────────────────────────────────────
        output.Content.SetHtmlContent($"""
            <template x-teleport="body">
            <div x-show="{Open}" x-cloak
                 x-effect="window.__modalScrollLock && window.__modalScrollLock.set($el, {Open})"
                 class="fixed inset-0 z-50 flex items-stretch justify-center sm:items-center sm:p-4"
                 {escapeClose}x-on:close-modals.window="{close}">
                <div x-show="{Open}"
                     x-transition:enter="ease-out duration-200" x-transition:enter-start="opacity-0" x-transition:enter-end="opacity-100"
                     x-transition:leave="ease-in duration-150" x-transition:leave-start="opacity-100" x-transition:leave-end="opacity-0"
                     class="fixed inset-0 bg-slate-900/60 backdrop-blur-sm"{backdropClose}></div>

                <div x-show="{Open}"
                     x-transition:enter="ease-out duration-200" x-transition:enter-start="opacity-0 sm:scale-95" x-transition:enter-end="opacity-100 sm:scale-100"
                     x-transition:leave="ease-in duration-150" x-transition:leave-start="opacity-100 sm:scale-100" x-transition:leave-end="opacity-0 sm:scale-95"
                     class="relative flex w-full flex-col overflow-hidden bg-white shadow-xl {(Tall ? "sm:my-4 sm:h-[92vh]" : "sm:my-8 sm:h-auto sm:max-h-[90vh]")} sm:w-full {maxWidth} sm:rounded-xl"{panelStyleAttr}>
                    {cornerClose}
                    {(hasHeader ? $"""
                    <div class="flex-shrink-0 bg-white border-b border-slate-100 p-6">
                        <div class="flex items-center justify-between gap-4">
                            <h2 class="text-2xl font-bold text-slate-900">{titleHtml}</h2>
                            <button type="button" x-on:click="{close}" class="bg-rose-50 text-rose-600 hover:bg-rose-600 hover:text-white border border-rose-200/80 p-2 rounded-xl transition-all shadow-sm focus:outline-none shrink-0">
                                <span class="sr-only">Fermer</span>
                                <svg aria-hidden="true" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"></path></svg>
                            </button>
                        </div>
                        {(subtitle is null ? "" : $"""<div class="mt-2 text-sm leading-relaxed text-slate-600">{subtitle}</div>""")}
                    </div>
                    """ : "")}
                    <div class="relative flex-1 overflow-y-auto min-h-0 {bodyClasses}">
                        {body}
                    </div>
                    {(footer is null ? "" : $"""
                    <div class="flex-shrink-0 bg-white border-t border-slate-100 p-4 px-6 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between sm:gap-4">
                        {footer}
                    </div>
                    """)}
                </div>
            </div>
            </template>
            """);
    }

    /// <summary>Classes littérales (jamais interpolées) pour que Tailwind les voie au scan du CSS.</summary>
    private static string MaxWidthClass(string size) => size.ToLowerInvariant() switch
    {
        "md" => "sm:max-w-md",
        "lg" => "sm:max-w-lg",
        "xl" => "sm:max-w-xl",
        "2xl" => "sm:max-w-2xl",
        // Fiches de détail (élève, enseignant) : un cran sous `wide` — assez large pour une grille à
        // 4 colonnes, sans occuper presque tout l'écran.
        "5xl" => "sm:max-w-5xl",
        // Aperçu PDF : plafond 4xl (56rem / 896px). La visionneuse native centre la page sur un fond
        // sombre — au-delà de cette largeur, ce ne sont plus que deux larges bandes vides de part et
        // d'autre du document. Combiné à `tall` (≈ 92 vh), la page respire sans que la modale s'étale.
        "pdf" => "sm:max-w-4xl",
        "60" => "sm:w-[60%] sm:max-w-4xl",
        "wide" => "sm:w-[85%] sm:max-w-6xl",
        _ => "sm:max-w-3xl"
    };
}

/// <summary>
/// Sous-titre libre du panneau (texte fixe ou lié en Alpine, ex. <c>x-text="detailStudent.matricule"</c>).
/// N'émet rien à sa propre place : son contenu est absorbé par <see cref="ModalShellTagHelper"/> parent,
/// qui le replace dans l'en-tête.
/// </summary>
[HtmlTargetElement("modal-subtitle", ParentTag = "modal-shell")]
public class ModalSubtitleTagHelper : TagHelper
{
    internal const string ItemsKey = "SamaEcole.ModalSubtitle";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        context.Items[ItemsKey] = (await output.GetChildContentAsync()).GetContent();
        output.SuppressOutput();
    }
}

/// <summary>
/// Titre HTML libre du panneau, pour un titre DYNAMIQUE que l'attribut <c>title</c> (texte encodé) ne
/// peut pas porter — ex. « Montant standard — <span x-text="selectedCategory.name"></span> ». Même
/// mécanisme que <see cref="ModalSubtitleTagHelper"/> : le contenu est absorbé par le parent et replacé
/// dans le &lt;h2&gt; de l'en-tête. S'il est présent, il l'emporte sur l'attribut <c>title</c>.
/// </summary>
[HtmlTargetElement("modal-title", ParentTag = "modal-shell")]
public class ModalTitleTagHelper : TagHelper
{
    internal const string ItemsKey = "SamaEcole.ModalTitle";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        context.Items[ItemsKey] = (await output.GetChildContentAsync()).GetContent();
        output.SuppressOutput();
    }
}

/// <summary>
/// Footer de la modale. Extrait de son emplacement et positionné en bas de la modale de manière "sticky",
/// hors de la zone scrollable.
/// </summary>
[HtmlTargetElement("modal-footer", ParentTag = "modal-shell")]
public class ModalFooterTagHelper : TagHelper
{
    internal const string ItemsKey = "SamaEcole.ModalFooter";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        context.Items[ItemsKey] = (await output.GetChildContentAsync()).GetContent();
        output.SuppressOutput();
    }
}
