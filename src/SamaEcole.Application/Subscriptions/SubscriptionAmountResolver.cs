using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Subscriptions;

/// <param name="Amount">Montant en FCFA à régler.</param>
/// <param name="BillingPeriod">Période facturée : celle qui a été demandée (mensuelle ou annuelle).</param>
/// <param name="FromGrid">Vrai si le montant vient de la grille (offre déclarée à l'inscription), faux pour la tarification par plan.</param>
public record ResolvedSubscriptionAmount(decimal Amount, BillingPeriod BillingPeriod, bool FromGrid);

/// <summary>
/// UNE SEULE source du montant d'un abonnement, partagée par l'aperçu du code promo et l'initiation du paiement :
/// aucune des deux ne peut donc dériver de l'autre.
///
/// Un établissement issu d'une demande d'inscription approuvée est facturé selon SON OFFRE (public/privé × cycles ×
/// taille — la grille de la vitrine) :
///   · privé   → annuel : le forfait de la grille ; mensuel : ce forfait ÷ 12 arrondi (voir ISubscriptionPricingProvider) ;
///   · public  → refusé (422) : le tarif est par élève, dans une fourchette, et s'établit sur devis — pas de paiement
///     en ligne, le Super Admin active l'abonnement (accès gracieux) une fois le devis accepté.
/// Un abonnement sans demande d'inscription (comptes historiques, jeux de données de démonstration) garde la
/// tarification par plan : rien ne change pour eux.
///
/// L'offre est relue en base à chaque appel, jamais fournie par le client (AGENTS.md règle #10).
/// </summary>
public class SubscriptionAmountResolver(IApplicationDbContext dbContext, ISubscriptionPricingProvider pricingProvider)
{
    public async Task<ResolvedSubscriptionAmount> ResolveAsync(
        Guid schoolId, SubscriptionPlan plan, BillingPeriod requested, CancellationToken cancellationToken)
    {
        var offer = await dbContext.SchoolRegistrationRequests.AsNoTracking()
            .Where(r => r.CreatedSchoolId == schoolId && r.Status == RegistrationRequestStatus.Approved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Ownership, r.CycleProfile, r.SizeTier })
            .FirstOrDefaultAsync(cancellationToken);

        if (offer is null)
        {
            return new ResolvedSubscriptionAmount(pricingProvider.GetAmount(plan, requested), requested, FromGrid: false);
        }

        if (offer.Ownership == SchoolOwnership.Public)
        {
            throw new ValidationException([
                new ValidationFailure("Ownership",
                    "Le tarif des établissements publics est établi par élève, sur devis : contactez-nous pour finaliser votre abonnement.")
            ]);
        }

        if (offer.SizeTier is not { } tier)
        {
            throw new ValidationException([
                new ValidationFailure("SizeTier",
                    "La taille de votre établissement n'est pas renseignée : contactez-nous pour établir votre tarif.")
            ]);
        }

        return new ResolvedSubscriptionAmount(
            pricingProvider.GetGridAmount(offer.CycleProfile, tier, requested), requested, FromGrid: true);
    }
}
