using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Carte statistique partagée (Phase 1 de la refonte UI/UX, regabaritée par l'audit 2026). Réconcilie
/// les idiomes qui coexistaient pour le même besoin — la grande carte KPI 4-en-ligne de l'écran Élèves,
/// le bandeau compact « libellé/valeur » collé aux tableaux (Classes, Matières, Frais), et les cartes
/// écrites à la main dans le Tableau de bord et la Fiscalité — en un seul composant piloté par
/// <see cref="Size"/>.
///
/// La VALEUR est le contenu enfant, pas un attribut : elle vient presque toujours d'une liaison
/// Alpine (<c>x-text="totalCount"</c>) qu'un attribut C# ne saurait pas porter. Le libellé, l'icône
/// (nom d'un &lt;symbol&gt; du sprite) et la teinte d'accent sont des attributs simples.
///
/// Usage :
/// <code>
/// &lt;stat-card label="Total Élèves" icon="users" accent="primary"&gt;
///     &lt;span x-text="totalCount"&gt;0&lt;/span&gt;
///     &lt;stat-hint&gt;Garçons : &lt;span x-text="boys"&gt;&lt;/span&gt;&lt;/stat-hint&gt;
/// &lt;/stat-card&gt;
/// </code>
///
/// <para>
/// Le gabarit vit dans <c>.kpi-*</c> (input.css), jamais en dur ici : une carte de la Trésorerie et
/// une carte du Tableau de bord doivent partager le même rayon, la même bordure et la même graisse,
/// même si personne ne les regarde côte à côte.
/// </para>
/// </summary>
[HtmlTargetElement("stat-card")]
public class StatCardTagHelper : TagHelper
{
    /// <summary>Libellé au-dessus de la valeur (ex. « Total Élèves »). Texte simple, encodé.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Nom d'icône du sprite (ex. "users" → #icon-users). Optionnel : sans icône, pas de pastille.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Teinte de la pastille d'icône et de la valeur. Un jeton sémantique (primary/success/warning/danger)
    /// ou une teinte de série (blue/pink/slate) pour les cas Élèves (filles/garçons). Défaut : primary.
    /// Reste dans la palette, jamais d'hexadécimal en dur.
    /// </summary>
    public string Accent { get; set; } = "primary";

    /// <summary>"lg" (grande carte KPI autonome) ou "compact" (cellule d'un bandeau de synthèse). Défaut : lg.</summary>
    public string Size { get; set; } = "lg";

    /// <summary>
    /// Indicateur MAJEUR : ajoute le liseré d'accent à gauche et une ombre plus marquée, pour hiérarchiser
    /// une grille qui mélange indicateurs de tête et indicateurs secondaires (Tableau de bord financier).
    /// </summary>
    public bool Emphasis { get; set; }

    /// <summary>
    /// La carte est réellement cliquable (l'appelant pose alors son propre <c>x-on:click</c> / <c>href</c>).
    /// Sans cet attribut, la carte reçoit un survol discret mais AUCUN <c>cursor-pointer</c> : un curseur
    /// de main sur un bloc qui ne réagit pas au clic est un mensonge d'interface.
    /// </summary>
    public bool Interactive { get; set; }

    /// <summary>
    /// Expression Alpine produisant la classe de couleur de la VALEUR, quand celle-ci dépend de la donnée
    /// (ex. un solde net vert au-dessus de zéro, rose en dessous). Quand elle est fournie, la couleur
    /// statique de <see cref="Accent"/> n'est pas posée sur la valeur — sinon les deux classes
    /// coexisteraient et c'est l'ordre du CSS, pas la donnée, qui trancherait.
    /// </summary>
    public string? ValueClassExpr { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var value = (await output.GetChildContentAsync()).GetContent();
        var hint = context.Items.TryGetValue(StatHintTagHelper.ItemsKey, out var h) ? (string)h! : null;
        var label = WebUtility.HtmlEncode(Label);
        var isCompact = string.Equals(Size, "compact", StringComparison.OrdinalIgnoreCase);

        var (chipClass, valueClass) = AccentClasses(Accent);

        output.TagName = null; // pas de <stat-card> littéral

        if (isCompact)
        {
            // Bandeau de synthèse : libellé discret + valeur, sans carte ni liseré. Pensé pour vivre
            // dans un conteneur `flex gap-6` collé au-dessus d'un tableau.
            var compactIcon = string.IsNullOrWhiteSpace(Icon) ? "" : $"""
                <span class="icon-chip-sm {chipClass}">
                    <svg class="h-4 w-4" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><use href="#icon-{WebUtility.HtmlEncode(Icon)}"></use></svg>
                </span>
                """;

            output.Content.SetHtmlContent($"""
                <div class="flex items-center gap-3">
                    {compactIcon}
                    <div class="min-w-0">
                        <p class="kpi-label">{label}</p>
                        <p class="text-lg font-bold tabular-nums text-slate-900">{value}</p>
                    </div>
                </div>
                """);
            return;
        }

        // Grande carte KPI autonome (grille `sm:grid-cols-2 lg:grid-cols-4`).
        var largeIcon = string.IsNullOrWhiteSpace(Icon) ? "" : $"""
            <span class="icon-chip-md {chipClass}">
                <svg class="h-5 w-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><use href="#icon-{WebUtility.HtmlEncode(Icon)}"></use></svg>
            </span>
            """;

        // Survol systématique mais SOBRE ; le curseur de main n'apparaît que sur une carte cliquable.
        var hover = "transition-all duration-200 ease-out hover:-translate-y-0.5 hover:border-slate-300/70 hover:shadow-md"
                    + (Interactive ? " cursor-pointer" : "");
        var emphasis = Emphasis ? $" {AccentBorderClass(Accent)} shadow-md" : "";

        // Une expression dynamique remplace la couleur statique — jamais les deux (voir ValueClassExpr).
        var valueColour = string.IsNullOrWhiteSpace(ValueClassExpr) ? $" {valueClass}" : "";
        var valueBinding = string.IsNullOrWhiteSpace(ValueClassExpr) ? "" : $""" :class="{ValueClassExpr}" """;

        var hintHtml = hint is null ? "" : $"""<p class="kpi-hint">{hint}</p>""";

        output.Content.SetHtmlContent($"""
            <div class="kpi-card {hover}{emphasis}">
                <div class="min-w-0">
                    <p class="kpi-label">{label}</p>
                    <p class="kpi-value{valueColour}"{valueBinding}>{value}</p>
                    {hintHtml}
                </div>
                {largeIcon}
            </div>
            """);
    }

    /// <summary>
    /// Traduit le nom d'accent en classe de pastille ET en classe de couleur de la valeur — les deux
    /// restent volontairement liées (même famille de teinte pour l'icône et le chiffre). Classes écrites
    /// EN CLAIR (littéraux complets, jamais "text-" + accent) : le scanner Tailwind lit ce fichier comme
    /// du texte brut (voir `content` dans tailwind.config.js) et n'indexe que les classes qui y
    /// apparaissent telles quelles.
    ///
    /// La valeur prend le ton 700 et la pastille le ton 600 : à 24px gras, le ton 700 tient le seuil AA
    /// du texte courant (4.5:1) et pas seulement celui du grand texte, tandis que l'icône — élément
    /// graphique, seuil 3:1 — reste vive sur son fond en ton 50.
    /// </summary>
    private static (string chip, string value) AccentClasses(string accent) => accent.ToLowerInvariant() switch
    {
        "success" => ("icon-chip-emerald", "text-emerald-700"),
        "warning" => ("icon-chip-amber", "text-amber-700"),
        "danger" => ("icon-chip-rose", "text-rose-700"),
        "pink" => ("icon-chip-pink", "text-pink-700"),
        "blue" => ("icon-chip-blue", "text-blue-700"),
        "purple" => ("icon-chip-purple", "text-purple-700"),
        "slate" => ("icon-chip-slate", "text-slate-900"),
        "primary" => ("icon-chip-indigo", "text-indigo-700"),
        _ => ("icon-chip-indigo", "text-indigo-700")
    };

    /// <summary>Liseré d'accent d'un indicateur majeur (voir <see cref="Emphasis"/>).</summary>
    private static string AccentBorderClass(string accent) => accent.ToLowerInvariant() switch
    {
        "success" => "kpi-accent-success",
        "warning" => "kpi-accent-warning",
        "danger" => "kpi-accent-danger",
        _ => "kpi-accent-primary"
    };
}

/// <summary>
/// Indication secondaire d'une carte KPI (« Garçons : 312 | Filles : 289 »), en HTML libre pour porter
/// des liaisons Alpine. N'émet rien à sa propre place : son contenu est absorbé par le
/// <see cref="StatCardTagHelper"/> parent, qui le replace sous la valeur — même mécanisme que
/// &lt;modal-subtitle&gt; (voir ModalShellTagHelper).
/// </summary>
[HtmlTargetElement("stat-hint", ParentTag = "stat-card")]
public class StatHintTagHelper : TagHelper
{
    internal const string ItemsKey = "SamaEcole.StatHint";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        context.Items[ItemsKey] = (await output.GetChildContentAsync()).GetContent();
        output.SuppressOutput();
    }
}
