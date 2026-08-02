namespace SamaEcole.Domain.Entities;

/// <summary>
/// Fiche d'un établissement telle qu'elle est exposée dans l'ANNUAIRE PUBLIC (B2C) — adossée à la vue
/// <c>public_school_directory</c> (migration AddPublicSchoolDirectory), jamais à la table
/// <c>schools</c> directement.
///
/// Entité de LECTURE SEULE, sans clé, sur le modèle de <see cref="PlatformDashboardStats"/> : elle
/// n'est jamais écrite, jamais suivie par le change tracker, et ne porte aucun champ d'audit.
///
/// Ce type ne contient QUE des données de vitrine, et c'est structurel : la vue qui l'alimente fige
/// la liste des colonnes et n'expose ni NINEA, ni RCCM, ni statut, ni en-têtes administratifs. Ajouter
/// une propriété ici ne suffirait donc pas à faire fuiter une donnée — il faudrait modifier la vue par
/// une nouvelle migration, geste explicite et relu.
/// </summary>
public class PublicSchoolListing
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? City { get; set; }

    public string? Region { get; set; }

    /// <summary>Présentation libre rédigée par le Directeur. Null s'il n'a rien saisi.</summary>
    public string? PublicDescription { get; set; }

    public string? LogoUrl { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>
    /// Cycles réellement proposés (« Prescolaire », « Primaire », « College », « Lycee »), DÉRIVÉS des
    /// classes ouvertes par l'établissement — jamais une liste déclarative saisie à la main, qui
    /// deviendrait fausse dès la première rentrée. Vide tant qu'aucune classe n'existe.
    /// </summary>
    public string[] Cycles { get; set; } = [];
}
