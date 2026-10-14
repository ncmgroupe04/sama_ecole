using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Subscriptions.Pricing;

namespace SamaEcole.Web.ViewComponents;

/// <param name="Label">« Jusqu'à 100 élèves », « 101 à 150 élèves »… — les bornes viennent de la grille elle-même.</param>
/// <param name="PaidMonths">« 10 mois payés » : le forfait annuel est une remise d'engagement, pas un mensuel × 12.</param>
public sealed record PrivatePricingCard(string Tier, string Label, string Monthly, string Annual, string PaidMonths, string RegisterHref);

/// <param name="QuoteAboveStudents">Au-delà : offre « Grand Groupe », sur devis.</param>
/// <param name="BoardingNote">Phrase sur la majoration Internat ; <c>null</c> tant qu'aucune n'est fixée.</param>
public sealed record PrivatePricingModel(IReadOnlyList<PrivatePricingCard> Cards, int QuoteAboveStudents, string? BoardingNote)
{
    /// <summary>
    /// Construit les cartes d'après la grille publique — la MÊME que renvoie GET /api/v1/public/pricing-grid, donc celle
    /// que le formulaire d'inscription affiche et que le serveur facture. Aucun montant n'est écrit dans la vitrine.
    /// </summary>
    public static PrivatePricingModel From(PublicPricingGridDto grid)
    {
        var cards = new List<PrivatePricingCard>();
        int? previousMax = null;

        foreach (var tier in grid.Tiers)
        {
            cards.Add(new PrivatePricingCard(
                tier.Tier,
                previousMax is null ? $"Jusqu'à {tier.MaxStudents} élèves" : $"{previousMax + 1} à {tier.MaxStudents} élèves",
                $"{FormatXof(tier.MonthlyXof)} / mois",
                $"{FormatXof(tier.AnnualXof)} / an",
                PaidMonths(tier.MonthlyXof, tier.AnnualXof),
                $"/inscription?type=prive&tranche={tier.Tier}"));

            previousMax = tier.MaxStudents;
        }

        var surcharged = grid.Tiers.Where(t => t.BoardingSurchargeMonthlyXof > 0 || t.BoardingSurchargeAnnualXof > 0).ToList();

        // Une majoration par tranche peut différer : on n'annonce un montant que s'il est le même partout, sinon la
        // phrase reste qualitative plutôt que fausse.
        var note = surcharged.Count == 0
            ? null
            : surcharged.Select(t => t.BoardingSurchargeMonthlyXof).Distinct().Count() == 1 && surcharged.Count == grid.Tiers.Count
                ? $"Établissement avec internat : majoration de {FormatXof(surcharged[0].BoardingSurchargeMonthlyXof)} par mois."
                : "Établissement avec internat : une majoration s'ajoute, précisée à l'inscription.";

        return new PrivatePricingModel(cards, grid.QuoteAboveStudents, note);
    }

    /// <summary>« 15 000 FCFA » avec espaces insécables : un montant ne se coupe jamais sur deux lignes.</summary>
    public static string FormatXof(decimal amount) =>
        amount.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")).Replace(' ', ' ') + " FCFA";

    private static string PaidMonths(decimal monthly, decimal annual)
    {
        if (monthly <= 0)
        {
            return string.Empty;
        }

        var months = Math.Round(annual / monthly, 1);
        var text = months == decimal.Truncate(months) ? ((int)months).ToString(CultureInfo.InvariantCulture) : months.ToString("0.0", CultureInfo.GetCultureInfo("fr-FR"));

        return months < 12 && months == decimal.Truncate(months)
            ? $"{text} mois payés, {12 - (int)months} offerts"
            : $"{text} mois payés";
    }
}

/// <summary>
/// Grille tarifaire PRIVÉE de la vitrine : une carte par tranche d'effectif, rendue côté serveur (sans JavaScript, comme le
/// reste de la grille) d'après la grille éditée par le Super Admin. Remplace les anciennes cartes par cycles : le prix d'un
/// établissement privé ne dépend plus que de son effectif.
/// </summary>
public class PrivatePricingViewComponent(ISender mediator) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var grid = await mediator.Send(new GetPublicPricingGridQuery());

        return View(PrivatePricingModel.From(grid));
    }
}
