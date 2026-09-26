using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Règles de la GRILLE TARIFAIRE publiée sur la vitrine (MarketingCatalog.Pricing) : quelles combinaisons
/// « public/privé × cycles × taille » existent, et quel plan d'abonnement — donc quels DROITS de fonctionnalités —
/// en découle. Les MONTANTS, eux, vivent dans la configuration (SubscriptionPricingOptions, appsettings.json).
///
/// Le demandeur ne choisit plus de « formule » : il décrit son établissement, et le plan est déduit ici.
/// Correspondance retenue (à confirmer commercialement — la grille ne promet aucune option payante) :
///   · un seul cycle, public ou privé  → <see cref="SubscriptionPlan.Primaire"/> : le socle métier ;
///   · bicycle et grand complexe       → <see cref="SubscriptionPlan.Standard"/> : ils ajoutent les rapports
///     financiers consolidés PAR CYCLE, dont l'usage n'a de sens qu'à partir de deux cycles.
/// Aucune offre de la grille n'inclut <see cref="SubscriptionPlan.Premium"/> (notifications SMS) : un
/// établissement qui les veut passe par un devis et un changement de plan par le Super Admin.
/// </summary>
public static class SubscriptionPricingGrid
{
    public static SubscriptionPlan PlanFor(SchoolCycleProfile profile) =>
        profile is SchoolCycleProfile.Bicycle or SchoolCycleProfile.Complexe
            ? SubscriptionPlan.Standard
            : SubscriptionPlan.Primaire;

    /// <summary>
    /// Vrai si la combinaison existe dans la grille. Public : un seul cycle, jamais de palier (facturé par
    /// élève). Privé : palier de taille obligatoire, tous les profils de cycles.
    /// </summary>
    public static bool IsValidCombination(SchoolOwnership ownership, SchoolCycleProfile profile, SchoolSizeTier? tier) =>
        ownership == SchoolOwnership.Public
            ? profile is SchoolCycleProfile.Primaire or SchoolCycleProfile.College or SchoolCycleProfile.Lycee
            : tier is not null;
}
