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
/// </summary>
[HtmlTargetElement("badge")]
public class BadgeTagHelper : TagHelper
{
    /// <summary>neutral (défaut) | success | warning | danger | primary.</summary>
    public string Variant { get; set; } = "neutral";

    public override void Process(TagHelperContext context, TagHelperOutput output)
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
    }
}
