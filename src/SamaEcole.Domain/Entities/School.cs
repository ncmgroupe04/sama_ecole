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

    /// <summary>Adresse e-mail de contact de l'établissement, imprimée dans l'en-tête du reçu.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Numéro d'Identification Nationale des Entreprises et Associations. Mention légale sénégalaise
    /// portée par l'en-tête du reçu : c'est ce qui distingue une pièce comptable opposable d'un simple
    /// justificatif interne. Null tant que le Directeur ne l'a pas saisi — la ligne s'imprime alors
    /// sans cette mention, jamais avec un numéro inventé.
    /// </summary>
    public string? Ninea { get; set; }

    /// <summary>Registre du Commerce et du Crédit Mobilier (RCCM) — même usage que <see cref="Ninea"/>.</summary>
    public string? RegistreCommerce { get; set; }

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
    /// Nom porté par la troisième ligne de l'en-tête du bulletin (ex. « Popenguine »). SANS son préfixe
    /// de cycle : celui-ci (« ÉCOLE ÉLÉMENTAIRE DE » / « COLLÈGE DE » / « LYCÉE DE ») est ajouté à
    /// l'impression selon le cycle de la CLASSE de l'élève — un même établissement édite des bulletins
    /// de CM2 comme de Terminale. Un préfixe malgré tout saisi ici est retiré par
    /// <c>SchoolHeading.StripCyclePrefix</c>, sans quoi un bulletin de 6e afficherait
    /// « COLLÈGE DE : LYCÉE DE POPENGUINE ».
    ///
    /// Distinct de <see cref="Name"/> : la raison sociale complète (« Complexe Privé… », affichée sur le
    /// reçu) n'a pas sa place sur le bulletin. Tant que ce champ est vide, la ligne s'imprime réduite à
    /// son préfixe — AUCUN repli sur Name, même partiel (voir ReportCardDocument.ComposeHeader).
    /// </summary>
    public string? NomLycee { get; set; }

    // ------------------------------------------------------------------ Annuaire public (B2C)

    /// <summary>
    /// Consentement du Directeur à figurer dans l'annuaire PUBLIC des établissements.
    ///
    /// FAUX par défaut, et ce défaut est une décision, pas une commodité : publier le nom, la ville et
    /// les coordonnées d'un établissement est une diffusion vers des tiers, qui n'a pas à découler
    /// implicitement d'une inscription à un logiciel de gestion. Seul le Directeur bascule ce drapeau,
    /// depuis Paramètres → Établissement.
    ///
    /// Une école retirée de l'annuaire (repassée à faux) disparaît immédiatement des réponses publiques :
    /// le filtre est appliqué à CHAQUE requête, jamais mis en cache côté serveur.
    /// </summary>
    public bool IsPubliclyListed { get; set; }

    /// <summary>
    /// Ville, en donnée STRUCTURÉE — contrairement à <see cref="Address"/> qui reste du texte libre
    /// destiné à l'impression. C'est le principal critère de recherche d'un parent dans l'annuaire, et
    /// filtrer par sous-chaîne sur une adresse libre donnerait des résultats faux (« Rue de Dakar » à
    /// Thiès). Alimentée à l'approbation depuis la demande d'inscription, qui la saisit déjà.
    /// </summary>
    public string? City { get; set; }

    /// <summary>Région administrative (Dakar, Thiès, Saint-Louis…) — même usage que <see cref="City"/>.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// Présentation rédigée par le Directeur pour l'annuaire public. N'apparaît sur AUCUN document
    /// officiel (reçu, bulletin, attestation) : c'est un texte de vitrine, pas une mention légale.
    /// Null tant qu'il n'a rien saisi — la fiche s'affiche alors sans paragraphe de présentation,
    /// jamais avec un texte inventé à sa place.
    /// </summary>
    public string? PublicDescription { get; set; }
}
