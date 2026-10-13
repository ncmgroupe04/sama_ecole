using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Pricing;

/// <summary>
/// Moteur de tarification par tranche d'effectif : lit la grille en base (éditée par le Super Admin) et en
/// tire un devis. UNE seule source des montants pour le formulaire d'inscription, l'approbation et le futur
/// passage payant — jamais un montant fourni par le client (AGENTS.md règle #10).
///
/// Distinct de <see cref="SubscriptionAmountResolver"/>, qui continue de servir la grille historique
/// (cycles × taille Petit/Moyen/Grand) des demandes d'inscription antérieures aux tranches.
/// </summary>
public class SubscriptionPricingEngine(IApplicationDbContext dbContext)
{
    public async Task<PricingQuote> QuoteAsync(StudentQuotaTier tier, bool hasBoarding, CancellationToken cancellationToken)
    {
        var row = QuotaTierResolver.RequiresQuote(tier)
            ? null
            : await dbContext.QuotaTierPrices.AsNoTracking().FirstOrDefaultAsync(p => p.Tier == tier, cancellationToken);

        return QuotaTierPricing.Quote(tier, row is null ? null : ToTierPrice(row), hasBoarding);
    }

    /// <summary>
    /// Vrai si l'école est facturée d'après la grille par TRANCHE : elle s'est inscrite avec une tranche, ou une demande
    /// de changement d'abonnement a été validée. Les autres suivent l'ancienne grille (cycles × taille) ou la tarification
    /// par formule, et n'ont pas de tarif comparable.
    /// </summary>
    public async Task<bool> IsBilledByTierAsync(Guid schoolId, CancellationToken cancellationToken) =>
        await dbContext.SchoolRegistrationRequests.AsNoTracking()
            .AnyAsync(r => r.CreatedSchoolId == schoolId && r.Status == RegistrationRequestStatus.Approved && r.Tier != null, cancellationToken)
        || await dbContext.SubscriptionChangeRequests.AsNoTracking()
            .AnyAsync(r => r.SchoolId == schoolId && r.Status == SubscriptionChangeStatus.Approved, cancellationToken);

    /// <summary>La grille complète, de la plus petite tranche à la plus grande.</summary>
    public async Task<IReadOnlyList<TierPrice>> GetGridAsync(CancellationToken cancellationToken) =>
        Order(await dbContext.QuotaTierPrices.AsNoTracking().ToListAsync(cancellationToken))
            .Select(ToTierPrice)
            .ToList();

    /// <summary>Les lignes de prix dans l'ordre de l'échelle des tranches, celles qui manquent en base étant ignorées.</summary>
    internal static IEnumerable<QuotaTierPrice> Order(IReadOnlyCollection<QuotaTierPrice> rows) =>
        QuotaTierPricing.PricedTiers()
            .Select(tier => rows.FirstOrDefault(r => r.Tier == tier))
            .OfType<QuotaTierPrice>();

    internal static TierPrice ToTierPrice(QuotaTierPrice p) =>
        new(p.Tier, p.MonthlyXof, p.AnnualXof, p.BoardingSurchargeMonthlyXof, p.BoardingSurchargeAnnualXof);
}
