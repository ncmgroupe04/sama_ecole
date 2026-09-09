using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Contenu standard d'une confirmation de succès après une action (création, approbation…) : icône ✓,
/// message, puis un ou deux boutons — "Retour" seul si l'action n'a rien à répéter (ex. approuver une
/// demande), ou "Retour" + "Nouveau X" quand elle enchaîne naturellement sur une saisie suivante (ex.
/// ajouter une autre classe). Ce n'est que le CONTENU intérieur : à placer dans un &lt;modal-shell&gt;
/// parent, qui porte le titre, la fermeture (croix/Échap/fond) et la taille — même répartition des
/// responsabilités que modal-subtitle/modal-title avec leur modal-shell parent.
///
/// Usage :
/// <code>
/// &lt;modal-shell open="showAddedDialog" size="md" on-close="showAddedDialog = false" title="Classe ajoutée"&gt;
///     &lt;confirm-dialog-body close-action="showAddedDialog = false"
///                          new-action="showAddedDialog = false; isCreateOpen = true" new-label="Nouvelle classe"&gt;
///         &lt;span x-text="addedClassroomName"&gt;&lt;/span&gt; figure désormais dans la liste.
///     &lt;/confirm-dialog-body&gt;
/// &lt;/modal-shell&gt;
/// </code>
///
/// <see cref="CloseAction"/> et <see cref="NewAction"/> sont des EXPRESSIONS Alpine.js, jamais des
/// données utilisateur — même convention que <see cref="ModalShellTagHelper"/>.
/// </summary>
[HtmlTargetElement("confirm-dialog-body")]
public class ConfirmDialogBodyTagHelper : TagHelper
{
    /// <summary>Expression Alpine exécutée par le bouton "Retour" (ex. "showAddedDialog = false").</summary>
    public string CloseAction { get; set; } = string.Empty;

    /// <summary>
    /// Libellé du bouton de fermeture. Non renseigné : « Retour » pour <c>success</c>, « Compris »
    /// pour <c>info</c> (une modale de guidage se ferme sur un accusé de lecture, pas un retour arrière).
    /// </summary>
    public string? CloseLabel { get; set; }

    /// <summary>
    /// <c>success</c> (défaut) : coche verte animée, pour confirmer une action réussie.
    /// <c>info</c> : pastille d'information bleu Unikol, pour une modale de GUIDAGE (un pré-requis
    /// manque, on explique l'étape à faire) — surtout PAS un rendu d'erreur système.
    /// </summary>
    public string Variant { get; set; } = "success";

    /// <summary>
    /// Expression Alpine optionnelle rendant un titre EN-TÊTE À L'INTÉRIEUR du bloc centré, juste
    /// au-dessus du message (ex. « $store.guide.title »). Pour une modale sans en-tête modal-shell
    /// (guidage) : le titre vit alors dans le corps, la bande blanche supérieure disparaît. Vide →
    /// aucun titre affiché. Expression Alpine, jamais une donnée utilisateur (comme <see cref="CloseAction"/>).
    /// </summary>
    public string? TitleExpr { get; set; }

    /// <summary>
    /// Expression Alpine exécutée par le second bouton (ex. "showAddedDialog = false; isCreateOpen = true").
    /// Absent (par défaut) : un seul bouton "Retour" est affiché, centré — cas d'une action ponctuelle
    /// sans suite naturelle (approuver une demande, rejeter…).
    /// </summary>
    public string? NewAction { get; set; }

    /// <summary>Libellé du second bouton (ex. "Nouvelle classe"), requis si <see cref="NewAction"/> est fourni.</summary>
    public string? NewLabel { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var message = (await output.GetChildContentAsync()).GetContent();

        output.TagName = null;

        var isInfo = string.Equals(Variant, "info", StringComparison.OrdinalIgnoreCase);
        var closeLabel = CloseLabel ?? (isInfo ? "Compris" : "Retour");

        // Titre optionnel rendu DANS le bloc centré (voir TitleExpr) : `x-show` sur la même
        // expression pour qu'un titre vide ne laisse pas d'espace mort au-dessus du message.
        var hasTitle = !string.IsNullOrWhiteSpace(TitleExpr);
        var titleBlock = hasTitle
            ? $"""<h2 x-show="{TitleExpr}" x-cloak class="mt-3 text-lg font-bold text-slate-900" x-text="{TitleExpr}"></h2>"""
            : "";

        var hasNewAction = !string.IsNullOrWhiteSpace(NewAction);
        var justify = hasNewAction ? "sm:justify-end" : "sm:justify-center";
        var buttons = hasNewAction
            ? $"""
                <button type="button" x-on:click="{CloseAction}" class="btn-secondary">{closeLabel}</button>
                <button type="button" x-on:click="{NewAction}" class="btn-primary">{NewLabel}</button>
                """
            : $"""<button type="button" x-on:click="{CloseAction}" class="btn-primary">{closeLabel}</button>""";

        // Pastille + glyphe selon le variant. `info` : cercle « i » bleu Unikol, statique (rien à
        // animer — ce n'est pas une récompense, juste un repère). Même contrainte que la coche :
        // <icon> ne se compile pas dans une chaîne HTML brute, d'où le SVG en clair.
        var iconBlock = isInfo
            ? """
                <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-primary-50">
                    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true" class="h-7 w-7 text-primary-600">
                        <circle cx="12" cy="12" r="9.25" stroke="currentColor" stroke-width="1.75" />
                        <path d="M12 11.25v5" stroke="currentColor" stroke-width="2.25" stroke-linecap="round" />
                        <circle cx="12" cy="7.75" r="1.15" fill="currentColor" />
                    </svg>
                </div>
                """
            : """
                <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-success-bg">
                    <svg viewBox="0 0 52 52" fill="none" aria-hidden="true" class="cdb-success-icon h-7 w-7 text-success">
                        <circle class="cdb-success-icon-circle" cx="26" cy="26" r="25" stroke="currentColor" stroke-width="2" />
                        <path class="cdb-success-icon-check" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" d="M14.1 27.2l7.1 7.2 16.7-16.8" />
                    </svg>
                </div>
                """;

        // <icon> est lui-même un TagHelper (IconTagHelper) : il ne se déclenche qu'à la COMPILATION
        // Razor d'un .cshtml, jamais sur une chaîne HTML brute produite ICI, à l'exécution, par
        // output.Content.SetHtmlContent. Un <icon name="checkmark-circle" /> injecté de cette façon
        // atterrit donc chez le navigateur tel quel — un élément inconnu, invisible : c'était le
        // cercle vide constaté (bg-success-bg affiché, mais aucune coche).
        //
        // Plutôt qu'un <use> statique vers le sprite Fluent (fill plein, rien à animer), un tracé
        // dédié en contour (cercle puis coche, stroke-dasharray/-dashoffset — voir .cdb-success-icon*
        // dans Styles/input.css) : le cercle se dessine, puis la coche, une seule fois à l'ouverture.
        // Couleur success (tailwind.config.js) portée sur le <svg> et héritée par stroke="currentColor".
        // Bloc compact et centré (demande produit) : icône plus ramassée, titre optionnel, message
        // lisible « sans effort » — text-base, font-medium, slate-800 très contrasté — et les
        // boutons rapprochés, sans filet séparateur qui allongeait le bloc.
        output.Content.SetHtmlContent($"""
            <div class="text-center">
                {iconBlock}
                {titleBlock}
                <p class="{(hasTitle ? "mt-1.5" : "mt-3")} text-base font-medium leading-relaxed text-slate-800">{message}</p>
            </div>
            <div class="mt-5 flex flex-col-reverse {justify} gap-3 sm:flex-row">
                {buttons}
            </div>
            """);
    }
}
