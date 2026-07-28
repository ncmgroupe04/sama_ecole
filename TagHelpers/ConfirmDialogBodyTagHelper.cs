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

    /// <summary>Libellé du bouton de fermeture.</summary>
    public string CloseLabel { get; set; } = "Retour";

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

        var hasNewAction = !string.IsNullOrWhiteSpace(NewAction);
        var justify = hasNewAction ? "sm:justify-end" : "sm:justify-center";
        var buttons = hasNewAction
            ? $"""
                <button type="button" x-on:click="{CloseAction}" class="btn-secondary">{CloseLabel}</button>
                <button type="button" x-on:click="{NewAction}" class="btn-primary">{NewLabel}</button>
                """
            : $"""<button type="button" x-on:click="{CloseAction}" class="btn-primary">{CloseLabel}</button>""";

        output.Content.SetHtmlContent($"""
            <div class="text-center">
                <div class="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-success-bg">
                    <icon name="checkmark-circle" class="h-8 w-8 text-success" />
                </div>
                <p class="mt-4 text-sm text-gray-500">{message}</p>
            </div>
            <div class="mt-8 flex flex-col-reverse {justify} gap-3 border-t border-gray-200 pt-4 sm:flex-row">
                {buttons}
            </div>
            """);
    }
}
