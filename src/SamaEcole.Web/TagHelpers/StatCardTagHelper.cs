using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Carte statistique partagée (Phase 1 de la refonte UI/UX). Réconcilie les DEUX idiomes qui
/// coexistaient jusqu'ici pour le même besoin — la grande carte KPI 4-en-ligne de l'écran Élèves et
/// le bandeau compact « libellé/valeur » collé aux tableaux (Classes, Matières, Frais) — en un seul
/// composant piloté par <see cref="Size"/>.
///
/// La VALEUR est le contenu enfant, pas un attribut : elle vient presque toujours d'une liaison
/// Alpine (<c>x-text="totalCount"</c>) qu'un attribut C# ne saurait pas porter. Le libellé, l'icône
/// (nom d'un &lt;symbol&gt; du sprite) et la teinte d'accent sont des attributs simples.
///
/// Usage :
/// <code>
/// &lt;stat-card label="Total Élèves" icon="users" accent="primary"&gt;
///     &lt;span x-text="totalCount"&gt;0&lt;/span&gt;
/// &lt;/stat-card&gt;
/// </code>
/// </summary>
[HtmlTargetElement("stat-card")]
public class StatCardTagHelper : TagHelper
{
    /// <summary>Libellé au-dessus de la valeur (ex. « Total Élèves »). Texte simple, encodé.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Nom d'icône du sprite (ex. "users" → #icon-users). Optionnel : sans icône, pas de pastille.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Teinte de la pastille d'icône. Un jeton sémantique (primary/success/warning/danger) ou "pink"/"blue"
    /// pour les cas Élèves (filles/garçons). Défaut : primary. Reste dans la palette, jamais d'hex en dur.
    /// </summary>
    public string Accent { get; set; } = "primary";

    /// <summary>"lg" (grande carte KPI autonome) ou "compact" (cellule d'un bandeau de synthèse). Défaut : lg.</summary>
    public string Size { get; set; } = "lg";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var value = (await output.GetChildContentAsync()).GetContent();
        var label = WebUtility.HtmlEncode(Label);
        var isCompact = string.Equals(Size, "compact", StringComparison.OrdinalIgnoreCase);

        var (pastilleBg, pastilleText) = AccentClasses(Accent);

        output.TagName = null; // pas de <stat-card> littéral

        if (isCompact)
        {
            // Bandeau de synthèse : libellé discret + valeur, sans carte ni pastille (l'icône reste
            // possible mais rare ici). Pensé pour vivre dans un conteneur `flex gap-6`.
            var compactIcon = string.IsNullOrWhiteSpace(Icon) ? "" : $"""
                <span class="flex h-8 w-8 items-center justify-center rounded-full {pastilleBg} {pastilleText}">
                    <svg class="h-4 w-4" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><use href="#icon-{WebUtility.HtmlEncode(Icon)}"></use></svg>
                </span>
                """;

            output.Content.SetHtmlContent($"""
                <div class="flex items-center gap-3">
                    {compactIcon}
                    <div>
                        <p class="text-xs font-medium uppercase tracking-wider text-gray-500">{label}</p>
                        <p class="text-lg font-bold text-gray-900">{value}</p>
                    </div>
                </div>
                """);
            return;
        }

        // Grande carte KPI autonome (grille `sm:grid-cols-2 lg:grid-cols-4`).
        var largeIcon = string.IsNullOrWhiteSpace(Icon) ? "" : $"""
            <div class="flex h-10 w-10 items-center justify-center rounded-full {pastilleBg} {pastilleText}">
                <svg class="h-5 w-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><use href="#icon-{WebUtility.HtmlEncode(Icon)}"></use></svg>
            </div>
            """;

        output.Content.SetHtmlContent($"""
            <div class="card flex items-center justify-between p-5 transition-shadow hover:shadow-md">
                <div>
                    <p class="mb-1 text-sm font-medium text-gray-500">{label}</p>
                    <p class="text-2xl font-bold text-gray-900">{value}</p>
                </div>
                {largeIcon}
            </div>
            """);
    }

    /// <summary>
    /// Traduit le nom d'accent en classes de pastille. Les jetons de marque/statut utilisent leurs
    /// variantes `-bg` (Phase 0) ; pink/blue restent des cas particuliers de l'écran Élèves.
    /// </summary>
    private static (string bg, string text) AccentClasses(string accent) => accent.ToLowerInvariant() switch
    {
        "success" => ("bg-success-bg", "text-success"),
        "warning" => ("bg-warning-bg", "text-warning"),
        "danger" => ("bg-danger-bg", "text-danger"),
        "pink" => ("bg-pink-50", "text-pink-600"),
        "blue" => ("bg-blue-50", "text-blue-600"),
        _ => ("bg-primary-50", "text-primary")
    };
}
