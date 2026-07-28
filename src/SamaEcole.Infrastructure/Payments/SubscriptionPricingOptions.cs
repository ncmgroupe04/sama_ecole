namespace SamaEcole.Infrastructure.Payments;

/// <summary>
/// Section "SubscriptionPricing" de la configuration (appsettings.json) — ticket JGK-I05.
///
/// ⚠️ VALEURS PLACEHOLDER — À AJUSTER AVANT PRODUCTION. Aucun tarif réel n'est fixé nulle part dans
/// docs/Volume_1_Cahier_des_Charges.md ni dans le schéma (docs/Volume_3_DDS.md) : SubscriptionPlan est
/// un simple enum, sans prix associé. Les montants ci-dessous sont des exemples plausibles pour
/// permettre au parcours de paiement de fonctionner de bout en bout dès aujourd'hui ; ils doivent être
/// remplacés par les tarifs réels (décision commerciale) avant toute mise en production.
/// </summary>
public class SubscriptionPricingOptions
{
    public const string SectionName = "SubscriptionPricing";

    public PlanPricing Primaire { get; set; } = new() { Monthly = 15_000m, Yearly = 150_000m };
    public PlanPricing Standard { get; set; } = new() { Monthly = 25_000m, Yearly = 250_000m };
    public PlanPricing Premium { get; set; } = new() { Monthly = 40_000m, Yearly = 400_000m };
}

/// <summary>Montants en FCFA.</summary>
public class PlanPricing
{
    public decimal Monthly { get; set; }
    public decimal Yearly { get; set; }
}
