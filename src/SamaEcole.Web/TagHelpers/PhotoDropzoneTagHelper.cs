using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Feature B — zone de glisser-déposer partagée (Élèves, Enseignants), création comme modification.
/// Compresse la photo CÔTÉ CLIENT (wwwroot/js/photo-compress.js, Canvas 300×300 JPEG q80, ~30 Ko) avant
/// d'exécuter <see cref="OnChange"/> — jamais de fichier brut envoyé au serveur.
///
/// <see cref="Preview"/> et <see cref="OnChange"/> sont des EXPRESSIONS Alpine brutes (même contrat que
/// <see cref="DateFieldTagHelper.Model"/> et <see cref="PaginationTagHelper.OnChange"/>), émises telles
/// quelles : ce composant ne connaît pas le formulaire qui l'englobe. <c>OnChange</c> s'exécute avec une
/// variable LOCALE <c>photoBase64</c> dans sa portée (le base64 compressé, ou <c>null</c> après un
/// retrait) — exactement comme <c>date-field</c> expose <c>day.iso</c> à l'intérieur de son
/// <c>{{Model}}</c>. Deux usages typiques :
///   * Création (pas encore d'Id) : <c>OnChange="newStudent.photoData = photoBase64"</c> — juste un
///     champ de plus dans le payload de création, envoyé au clic sur « Enregistrer ».
///   * Édition (fiche déjà créée) : <c>OnChange="uploadStudentPhoto(photoBase64)"</c> — appelle
///     IMMÉDIATEMENT PUT /{id}/photo (commande dédiée, voir SetStudentPhotoCommand) : mélanger la photo
///     dans la sauvegarde générale de la fiche ferait perdre la photo à la moindre modification de nom
///     qui omettrait de la retransmettre.
///
/// Le bouton « Retirer » n'apparaît que si <see cref="Preview"/> est actuellement vrai (rien à retirer
/// sinon) — pas besoin d'un attribut séparé pour le masquer en création tant qu'aucune photo n'est
/// posée.
/// </summary>
[HtmlTargetElement("photo-dropzone")]
public class PhotoDropzoneTagHelper : TagHelper
{
    /// <summary>Expression Alpine de l'URL/data-URI actuellement affichée (ex. « newStudent.photoUrl »).</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>Instruction Alpine exécutée avec <c>photoBase64</c> en portée (chaîne ou <c>null</c>).</summary>
    public string OnChange { get; set; } = string.Empty;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        output.Content.SetHtmlContent($$"""
            <div x-data="{
                    _dragging: false, _processing: false, _error: null,
                    async _handleFiles(files) {
                        const file = files && files[0];
                        if (!file) return;
                        this._error = null;
                        this._processing = true;
                        try {
                            const photoBase64 = await window.photoCompress.compress(file);
                            {{OnChange}}
                        } catch (err) {
                            this._error = (err && err.message) || 'Impossible de traiter cette image.';
                        } finally {
                            this._processing = false;
                        }
                    },
                    _remove() {
                        const photoBase64 = null;
                        {{OnChange}}
                    }
                 }">
                <div class="flex items-center gap-3">
                    <div x-on:dragover.prevent="_dragging = true" x-on:dragleave.prevent="_dragging = false"
                         x-on:drop.prevent="_dragging = false; _handleFiles($event.dataTransfer.files)"
                         x-on:click="$refs.photoFileInput.click()"
                         :class="_dragging ? 'border-primary bg-primary/5' : 'border-gray-300 hover:border-primary'"
                         class="relative w-20 h-20 rounded-full border-2 border-dashed flex items-center justify-center cursor-pointer shrink-0 overflow-hidden transition-colors">
                        <img x-show="{{Preview}}" :src="{{Preview}}" alt="" class="w-full h-full object-cover" x-cloak>
                        <svg x-show="!({{Preview}})" aria-hidden="true" viewBox="0 0 24 24" fill="currentColor" class="w-7 h-7 text-gray-400"><use href="#icon-upload"></use></svg>
                        <div x-show="_processing" x-cloak class="absolute inset-0 bg-white/80 flex items-center justify-center">
                            <svg aria-hidden="true" class="animate-spin h-5 w-5 text-primary" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg>
                        </div>
                    </div>
                    <div class="flex-1 min-w-0">
                        <p class="text-sm text-gray-600">Glissez une photo ici, ou <span class="text-primary font-medium">cliquez pour choisir un fichier</span>.</p>
                        <p class="text-xs text-gray-400 mt-0.5">JPEG/PNG — recadrée et compressée automatiquement.</p>
                        <button type="button" x-show="{{Preview}}" x-cloak x-on:click="_remove()" class="mt-1 text-xs font-medium text-danger hover:underline">
                            Retirer la photo
                        </button>
                    </div>
                    <input type="file" accept="image/*" x-ref="photoFileInput" class="hidden" x-on:change="_handleFiles($event.target.files)">
                </div>
                <p x-show="_error" x-cloak x-text="_error" class="mt-1 text-xs text-danger"></p>
            </div>
            """);
    }
}
