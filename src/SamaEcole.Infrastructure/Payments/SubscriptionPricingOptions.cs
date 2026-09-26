namespace SamaEcole.Infrastructure.Payments;

/// <summary>
/// Section "SubscriptionPricing" de la configuration (appsettings.json) — ticket JGK-I05.
///
/// DEUX TARIFICATIONS, un seul point d'entrée (SubscriptionAmountResolver) :
///
///   · <see cref="Grid"/> — la GRILLE publiée sur la vitrine (MarketingCatalog.Pricing), forfaits ANNUELS des
///     établissements PRIVÉS par cycles gérés et taille. C'est la tarification de tout établissement issu d'une
///     demande d'inscription. Le public (par élève, fourchette 500 à 1 000 FCFA) n'y figure pas : il se chiffre
///     sur devis. Les montants ci-dessous DOIVENT rester identiques à ceux de la vitrine (garde-fou :
///     SubscriptionPricingGridConsistencyTests).
///
///   · <see cref="Primaire"/>, <see cref="Standard"/>, <see cref="Premium"/> — tarification HISTORIQUE par plan,
///     conservée pour les abonnements sans demande d'inscription (comptes antérieurs à la grille, jeux de
///     démonstration). VALEURS PLACEHOLDER : ne pas les présenter comme une grille commerciale.
/// </summary>
public class SubscriptionPricingOptions
{
    public const string SectionName = "SubscriptionPricing";

    public GridPricing Grid { get; set; } = new();

    public PlanPricing Primaire { get; set; } = new() { Monthly = 15_000m, Yearly = 150_000m };
    public PlanPricing Standard { get; set; } = new() { Monthly = 25_000m, Yearly = 250_000m };
    public PlanPricing Premium { get; set; } = new() { Monthly = 40_000m, Yearly = 400_000m };
}

/// <summary>Forfaits annuels PRIVÉS de la vitrine, en FCFA — un palier de taille par cycle géré.</summary>
public class GridPricing
{
    public TierPricing Primaire { get; set; } = new() { Small = 150_000m, Medium = 250_000m, Large = 350_000m };
    public TierPricing College { get; set; } = new() { Small = 250_000m, Medium = 400_000m, Large = 550_000m };
    public TierPricing Lycee { get; set; } = new() { Small = 300_000m, Medium = 450_000m, Large = 600_000m };

    /// <summary>Deux cycles : moins de 400 élèves / 400 à 800 / plus de 800.</summary>
    public TierPricing Bicycle { get; set; } = new() { Small = 450_000m, Medium = 650_000m, Large = 850_000m };

    /// <summary>Maternelle à Lycée : moins de 500 élèves / 500 à 1 000 / plus de 1 000.</summary>
    public TierPricing Complexe { get; set; } = new() { Small = 600_000m, Medium = 850_000m, Large = 1_200_000m };
}

/// <summary>Montants annuels en FCFA par palier de taille.</summary>
public class TierPricing
{
    public decimal Small { get; set; }
    public decimal Medium { get; set; }
    public decimal Large { get; set; }
}

/// <summary>Montants en FCFA.</summary>
public class PlanPricing
{
    public decimal Monthly { get; set; }
    public decimal Yearly { get; set; }
}
