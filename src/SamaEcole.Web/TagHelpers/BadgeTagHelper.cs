using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Pastille de statut partagée (Phase 1 de la refonte UI/UX). Unifie les deux traitements qui
/// divergeaient — texte-seul coloré (Frais) vs. vraie pastille cerclée (Inscriptions, Caisse, Années
/// scolaires) — sur les classes <c>.status-badge-*</c> déclarées en Phase 0 (input.css).
///
/// La couleur porte le SENS : un badge n'est jamais la seule information (Volume 5 §8, WCAG AA — la
/// couleur ne suffit pas), le texte enfant l'accompagne toujours.
///
/// Usage : <c>&lt;badge variant="success"&gt;Payé&lt;/badge&gt;</c> — variantes neutral (défaut),
/// success, warning, danger, primary.
///
/// <para>
/// <b>Puce d'état (<c>dot</c>, refonte 2026)</b> — <c>&lt;badge variant="success" dot&gt;Payé&lt;/badge&gt;</c>
/// préfixe le libellé d'un point lumineux à la couleur de la variante (voir <c>.status-dot</c>,
/// input.css). À réserver aux badges qui annoncent un ÉTAT (payé / en attente / impayé / actif) ;
/// un badge qui n'est qu'une étiquette (le niveau d'une classe, un module d'audit) reste sans puce,
/// sinon la puce cesse de signifier quoi que ce soit.
/// </para>
/// <para>
/// <b>Attention :</b> <c>dot</c> est incompatible avec un <c>x-text</c> posé sur le badge lui-même —
/// Alpine REMPLACE tous les enfants de l'élément, donc la puce disparaîtrait au premier rendu. Pour
/// un libellé dynamique, porter la liaison sur un enfant :
/// <c>&lt;badge variant="success" dot&gt;&lt;span x-text="statusLabel(row)"&gt;&lt;/span&gt;&lt;/badge&gt;</c>.
/// </para>
/// </summary>
[HtmlTargetElement("badge")]
public class BadgeTagHelper : TagHelper
{
    /// <summary>neutral (défaut) | success | warning | danger | primary.</summary>
    public string Variant { get; set; } = "neutral";

    /// <summary>
    /// Préfixe le libellé d'une puce d'état à la couleur de la variante. Faux par défaut : la puce
    /// est réservée aux badges qui annoncent un état, pas aux badges-étiquettes.
    /// </summary>
    public bool Dot { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";

        var cssClass = Variant.ToLowerInvariant() switch
        {
            "success" => "status-badge-success",
            "warning" => "status-badge-warning",
            "danger" => "status-badge-danger",
            "primary" => "status-badge-primary",
            _ => "status-badge-neutral"
        };

        // On PRÉPEND à la classe éventuellement fournie sur l'élément, sans l'écraser (l'appelant peut
        // ajouter une marge, etc.).
        var existing = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class",
            string.IsNullOrWhiteSpace(existing) ? cssClass : $"{cssClass} {existing}");

        if (!Dot)
        {
            return;
        }

        // La puce hérite de la couleur du texte du badge (bg-current) : aucune classe de couleur à
        // dupliquer ici, la variante reste la seule source.
        var content = (await output.GetChildContentAsync()).GetContent();
        output.Content.SetHtmlContent($"""<span class="status-dot" aria-hidden="true"></span>{content}""");
    }
}
