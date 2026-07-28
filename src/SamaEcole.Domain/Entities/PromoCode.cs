using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Table plateforme (comme <see cref="Subscription"/>/<see cref="School"/>) : un code promo n'a pas
/// de <c>SchoolId</c>, il n'est donc ni sous RLS ni <c>ITenantEntity</c>. Géré exclusivement par le
/// Super Admin.
/// </summary>
public class PromoCode : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public PromoDiscountType DiscountType { get; set; }

    /// <summary>Pourcentage ou montant FCFA selon <see cref="DiscountType"/> ; ignoré pour FreeTrialMonths/FullDiscount.</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// Nombre de mois pendant lesquels la réduction s'applique. Requis pour FreeTrialMonths/FullDiscount
    /// (détermine la durée de l'accès offert) ; optionnel pour Percentage/FixedAmount (null = un seul
    /// paiement réduit, pas de reconduction).
    /// </summary>
    public int? DurationMonths { get; set; }

    /// <summary>Limite globale d'utilisations, toutes écoles confondues. Null = illimité.</summary>
    public int? MaxUses { get; set; }
    public int CurrentUses { get; set; }

    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }

    public bool IsActive { get; set; } = true;
}
