using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Châssis commun aux deux documents imprimables au format A5 paysage : le reçu de paiement
/// (/caisse) et le reçu/attestation d'inscription (/inscriptions). Les deux partageaient, recopié à
/// l'identique, le conteneur (<c>id="receipt-printable"</c>, mêmes classes Tailwind), l'en-tête
/// (logo + nom d'établissement + coordonnées + mentions légales) et le pied de page (case « Fait à… »,
/// cachet officiel, deux blocs de signature). Seuls varient : les pastilles en haut à droite, la
/// pastille centrée optionnelle (N° d'inscription), le corps du document (tableau de règlement côté
/// caisse ; déclaration + échéancier côté inscription), le libellé du second signataire, et la mention
/// en bas de page.
///
/// IMPORTANT — deux choix de conception, tous deux dictés par des vérifications FAITES, pas supposées :
///
/// 1. Ce composant NE PORTE PAS la garde <c>&lt;template x-if="receipt"&gt;</c> : elle reste écrite en
///    clair dans chaque vue appelante, autour de <c>&lt;receipt-a5&gt;</c>. `x-show` n'est pas une
///    protection — Alpine évalue quand même les liaisons du sous-arbre dès le montage — et
///    `regression-guards.test.mjs` (garde-fou NULL_DEREF_DEBT) lit le TEXTE SOURCE des vues pour
///    vérifier cette garde : un Tag Helper qui l'émettrait lui-même la rendrait invisible à cette
///    analyse statique.
///
/// 2. Les sections variables (pastilles, mention de pied) sont découpées par MARQUEURS HTML
///    (<c>&lt;!--badges--&gt;…&lt;!--/badges--&gt;</c>), jamais par des Tag Helpers enfants séparés
///    communiquant via <c>TagHelperContext.Items</c> — le patron que suit pourtant
///    <see cref="ModalShellTagHelper"/> (<c>&lt;modal-subtitle&gt;</c> etc.) partout ailleurs dans ce
///    projet. Vérifié PAR INSTRUMENTATION DIRECTE (02-03/09/2026) sur le SDK de cette machine : un Tag
///    Helper enfant imbriqué reçoit un <c>TagHelperContext.Items</c> qui n'est PAS le même objet que
///    celui de son parent (deux hash codes différents, confirmés en traçant les deux) — le contenu écrit
///    par l'enfant n'atteint donc jamais le parent. C'est un défaut de la plateforme, PRÉEXISTANT et
///    INDÉPENDANT de ce composant : il rend d'ores et déjà muets tous les <c>&lt;modal-subtitle&gt;</c>,
///    <c>&lt;modal-title&gt;</c> et <c>&lt;modal-footer&gt;</c> du site (vérifié sur /convocations ET
///    sur une sonde isolée, sans aucun rapport avec /caisse ou /inscriptions). Le corriger touche un
///    mécanisme partagé par une dizaine d'écrans et dépasse le périmètre de cette factorisation — signalé
///    séparément. En attendant, un SEUL appel à <c>GetChildContentAsync()</c> — celui de ce Tag Helper
///    sur SES PROPRES enfants directs — est fiable (c'est ce que prouve déjà le corps du document, qui
///    s'affiche correctement) ; le découpage par marqueurs se fait ensuite en mémoire, sur cette unique
///    chaîne de confiance.
///
/// Usage (voir Views/Caisse/Index.cshtml et Views/Enrollments/Index.cshtml) :
/// <code>
/// &lt;template x-if="receipt"&gt;
///     &lt;receipt-a5 signatory="Le Caissier"&gt;
///         &lt;!--badges--&gt;
///             ... pastilles Alpine libres, en haut à droite ...
///         &lt;!--/badges--&gt;
///
///         ... corps du document : cartouche, tableau ou déclaration+échéancier ...
///
///         &lt;!--footer--&gt;
///             ... mention de bas de page, texte ou liaison Alpine (x-text sur un &lt;span&gt; interne) ...
///         &lt;!--/footer--&gt;
///     &lt;/receipt-a5&gt;
/// &lt;/template&gt;
/// </code>
/// </summary>
[HtmlTargetElement("receipt-a5")]
public class ReceiptA5TagHelper : TagHelper
{
    /// <summary>
    /// Racine Alpine du document (propriété portant schoolLogoUrl/schoolName…). Toujours « receipt »
    /// à ce jour sur les deux écrans ; paramétrable si un troisième document A5 apparaît un jour avec
    /// un autre nom d'état.
    /// </summary>
    public string Receipt { get; set; } = "receipt";

    /// <summary>
    /// Active la pastille « N° … » centrée dans l'en-tête (marqueur <c>center-badge</c>) et
    /// l'espacement associé — seul le reçu d'inscription l'utilise. Cette pastille et le padding qui
    /// l'accompagne (aération de l'en-tête, cf. Enrollments) sont un choix visuel PROPRE à ce
    /// document ; ne pas l'activer sur /caisse changerait un rendu déjà validé sans que rien ne le
    /// demande.
    /// </summary>
    public bool CenterBadge { get; set; }

    /// <summary>Libellé du second signataire du pied de page (« Le Caissier », « Le Secrétariat »…).</summary>
    public string Signatory { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var raw = (await output.GetChildContentAsync()).GetContent();

        var (badges, afterBadges) = ExtractMarked(raw, "badges");
        var (centerBadge, afterCenterBadge) = ExtractMarked(afterBadges, "center-badge");
        var (footerMention, body) = ExtractMarked(afterCenterBadge, "footer");

        // Aération de l'en-tête réservée au reçu d'inscription (cf. XML doc de CenterBadge) : reproduite
        // ici à l'identique de ce qu'était Views/Enrollments/Index.cshtml AVANT cette extraction, pour
        // que son rendu ne bouge pas d'un pixel (règle #12 — reproduction fidèle du design-reference).
        var headerPadding = CenterBadge ? "pb-2.5 pt-1.5" : "pb-3";
        var headerRelative = CenterBadge ? " relative" : "";
        var namePadding = CenterBadge ? " pr-24" : "";
        var signatory = WebUtility.HtmlEncode(Signatory);

        output.TagName = null;
        output.Content.SetHtmlContent($"""
            <div id="receipt-printable" class="receipt-a5 receipt-container bg-white border border-slate-200/80 mx-auto text-slate-800 shadow-sm rounded-xl flex flex-col justify-between min-h-[148mm]">

                <div class="receipt-header{headerRelative} flex items-start gap-4 border-b border-slate-200 {headerPadding}">
                    <img x-show="{Receipt}.schoolLogoUrl" x-cloak :src="{Receipt}.schoolLogoUrl" class="h-10 w-10 shrink-0 object-contain" alt="" />
                    <div class="min-w-0 flex-1{namePadding}">
                        <h2 class="text-lg font-bold text-slate-900 tracking-tight" x-text="{Receipt}.schoolName"></h2>
                        <p class="text-[10px] text-slate-500 mt-0.5" x-text="contactLine()"></p>
                        <p x-show="legalMentions()" x-cloak class="text-[10px] text-slate-500" x-text="legalMentions()"></p>
                    </div>
                    {centerBadge}
                    <div class="shrink-0 flex flex-col items-end gap-1">
                        {badges}
                    </div>
                </div>

                {body}

                <!-- Pied de page, ancré en bas (flex-col + justify-between du conteneur) -->
                <div class="receipt-footer mt-auto pt-2 border-t border-slate-200">
                    <div class="flex items-end gap-6 text-[10px]">
                        <div class="flex-1">
                            <p class="italic text-slate-500" x-text="faitMention()"></p>
                            <p class="mt-2.5 text-[9px] font-semibold uppercase tracking-wider text-slate-400">Cachet officiel</p>
                        </div>
                        <div class="flex-1 text-right">
                            <div class="h-6"></div>
                            <div class="border-t border-dashed border-slate-400 pt-1 text-[9px] font-semibold uppercase tracking-wider text-slate-500">{signatory}</div>
                        </div>
                        <div class="flex-1 text-right">
                            <div class="h-6"></div>
                            <div class="border-t border-dashed border-slate-400 pt-1 text-[9px] font-semibold uppercase tracking-wider text-slate-500">Le Directeur</div>
                        </div>
                    </div>
                    <p class="mt-2 text-center text-[8px] leading-tight text-slate-400">{footerMention}</p>
                </div>

            </div>
            """);
    }

    /// <summary>
    /// Retire et renvoie le contenu compris entre <c>&lt;!--{marker}--&gt;</c> et
    /// <c>&lt;!--/{marker}--&gt;</c>, ainsi que le reste de <paramref name="html"/> une fois ce
    /// segment (marqueurs compris) ôté. Absent → contenu vide, reste inchangé : chaque section est
    /// facultative (seul <c>center-badge</c> l'est vraiment à ce jour, mais aucune des trois ne doit
    /// être required pour rester robuste à un futur troisième document A5 qui n'en aurait pas besoin).
    /// </summary>
    private static (string Content, string Remainder) ExtractMarked(string html, string marker)
    {
        var open = $"<!--{marker}-->";
        var close = $"<!--/{marker}-->";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return ("", html);

        var contentStart = start + open.Length;
        var end = html.IndexOf(close, contentStart, StringComparison.Ordinal);
        if (end < 0) return ("", html);

        var content = html[contentStart..end];
        var remainder = string.Concat(html.AsSpan(0, start), html.AsSpan(end + close.Length));
        return (content, remainder);
    }
}
