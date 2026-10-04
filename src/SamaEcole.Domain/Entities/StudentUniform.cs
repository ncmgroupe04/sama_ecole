using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Dotation : la tenue due à UN élève pour UNE année, avec sa taille, sa facturation et sa remise (module Tenues).
///
/// Elle relie ce qui existe déjà sans rien y écrire :
///   • l'inscription (<see cref="Enrollment"/>) et ses lignes de frais, LUES pour décider du statut de facturation ;
///   • le stock, mouvementé uniquement par <c>StockLedger</c> : la remise écrit un <c>Sortie</c> et ne touche
///     jamais les compteurs du lot à la main.
///
/// Les libellés (<see cref="ItemLabel"/>, <see cref="SizeLabel"/>, <see cref="StudentLabel"/>) sont FIGÉS à la
/// remise, même idiome que <c>ItemAssignment.BeneficiaryLabel</c> : une fiche de remise réimprimée trois ans plus
/// tard doit être identique à celle qui a été signée.
///
/// Verrou optimiste xmin (règle #5) : deux remises sur la même ligne, ou une modification pendant une remise, ne
/// s'écrasent pas en silence (409). Aucune suppression physique ni logique par l'API : une ligne s'annule
/// (<see cref="StudentUniformStatus.Cancelled"/>) ou se retourne (<see cref="StudentUniformStatus.Returned"/>).
/// </summary>
public class StudentUniform : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public Guid EnrollmentId { get; set; }

    /// <summary>Année scolaire de l'inscription, copiée : elle borne les listes, les besoins et les purges.</summary>
    public Guid SchoolYearId { get; set; }

    public Guid UniformItemId { get; set; }

    /// <summary>Taille choisie ; <c>null</c> tant qu'elle n'est pas confirmée (la remise l'exige).</summary>
    public Guid? UniformVariantId { get; set; }

    /// <summary>Nombre d'exemplaires. Une seule taille par ligne.</summary>
    public int Quantity { get; set; } = 1;

    public StudentUniformStatus Status { get; set; } = StudentUniformStatus.Requested;

    public DateOnly? DeliveredOn { get; set; }

    /// <summary>Utilisateur qui a remis la tenue — issu du JWT, jamais du client.</summary>
    public Guid? DeliveredByUserId { get; set; }

    /// <summary>Mouvement <c>Sortie</c> du journal de stock produit par la remise.</summary>
    public Guid? DeliveryStockMovementId { get; set; }

    /// <summary>Mouvement <c>Entree</c> produit par le retour.</summary>
    public Guid? ReturnStockMovementId { get; set; }

    /// <summary>Calculé À LA REMISE (<c>UniformBillingCheck</c>) puis figé : un barème modifié ne le réécrit pas.</summary>
    public UniformBillingStatus BillingStatus { get; set; } = UniformBillingStatus.NotBilled;

    /// <summary>Ligne de frais de l'inscription qui prouve la facturation ; <c>null</c> si non facturée.</summary>
    public Guid? FeeLineId { get; set; }

    /// <summary>Motif d'une remise non facturée — obligatoire dans ce cas, absent sinon.</summary>
    public string? NotBilledReason { get; set; }

    public string? ReturnReason { get; set; }

    public string? ItemLabel { get; set; }

    public string? SizeLabel { get; set; }

    public string? StudentLabel { get; set; }
}
