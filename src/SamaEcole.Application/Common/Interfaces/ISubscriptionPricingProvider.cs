using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Ticket JGK-I05 — montant à facturer pour un plan et une période données. Aucune table de tarifs
/// n'existe dans docs/Volume_3_DDS.md (le plan d'un abonnement est un simple enum, sans prix associé
/// nulle part dans le schéma) : cette abstraction isole le Handler de la SOURCE des tarifs — aujourd'hui
/// un fichier de configuration (voir SamaEcole.Infrastructure), potentiellement une table de prix pilotée
/// par le Super Admin demain, sans jamais changer l'appelant.
/// </summary>
public interface ISubscriptionPricingProvider
{
    /// <summary>Montant en FCFA. Lève <see cref="KeyNotFoundException"/> si aucun tarif n'est configuré pour ce couple.</summary>
    decimal GetAmount(SubscriptionPlan plan, BillingPeriod billingPeriod);

    /// <summary>
    /// Montant d'un établissement PRIVÉ selon la grille de la vitrine (cycles × palier de taille), en FCFA.
    /// <see cref="BillingPeriod.Yearly"/> : le forfait annuel publié. <see cref="BillingPeriod.Monthly"/> : ce forfait
    /// ÉTALÉ sur 12 mois, arrondi au multiple supérieur de SubscriptionPricingOptions.Grid.MonthlyRoundingXof (le
    /// mensuel n'est pas publié : il se déduit du forfait, il ne s'y ajoute aucune majoration). Le public (par élève,
    /// en fourchette) n'a pas de montant fixe : il n'est pas concerné. Appelé via SubscriptionAmountResolver.
    /// </summary>
    decimal GetGridAmount(SchoolCycleProfile profile, SchoolSizeTier tier, BillingPeriod billingPeriod);
}
