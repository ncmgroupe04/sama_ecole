using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Établissement scolaire = unité d'isolation multi-tenant. Cette entité n'implémente PAS
/// ITenantEntity : elle définit le tenant, elle ne lui appartient pas.
/// Voir docs/Volume_3_DDS.md §Multi-tenant (cas particulier) et docs/ERD.md.
/// </summary>
public class School : AuditableEntity
{
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    /// <summary>
    /// Inspection d'Académie de rattachement — ligne « IA : … » de l'en-tête du bulletin
    /// (docs/design-references/bulletin-reference.png, ex. « Thies »). Null tant que le Directeur ne
    /// l'a pas renseignée dans Paramètres → Établissement : la ligne s'imprime alors vide, jamais
    /// une valeur inventée.
    /// </summary>
    public string? InspectionAcademie { get; set; }

    /// <summary>Inspection de l'Éducation et de la Formation — ligne « IEF : … » du bulletin (ex. « Mbour 1 »).</summary>
    public string? InspectionEducationFormation { get; set; }

    /// <summary>
    /// Nom porté par la ligne « LYCEE DE : … » du bulletin (ex. « Popenguine »). Distinct de
    /// <see cref="Name"/> : la raison sociale complète (« Complexe Privé… », affichée sur le reçu)
    /// n'a pas sa place sur le bulletin. Tant que ce champ est vide, la ligne s'imprime vide — AUCUN
    /// repli sur Name, même partiel (voir ReportCardDocument.ComposeHeader).
    /// </summary>
    public string? NomLycee { get; set; }
}
