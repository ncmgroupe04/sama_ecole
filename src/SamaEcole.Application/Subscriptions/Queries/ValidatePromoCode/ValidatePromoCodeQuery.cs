using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subscriptions.Queries.ValidatePromoCode;

/// <summary>
/// POST /subscriptions/validate-promo — aperçu en direct, appelé depuis le tunnel de paiement
/// d'abonnement (SubscriptionPending.cshtml) avant de valider. LECTURE SEULE (AGENTS.md règle #7) :
/// n'incrémente JAMAIS PromoCode.CurrentUses, ne modifie rien — seul InitiateSubscriptionPaymentHandler
/// écrit, sur re-validation serveur indépendante (jamais le montant prévisualisé ici, règle #10).
///
/// Le SchoolId/Plan viennent de ITenantProvider + de l'abonnement en base, jamais du corps de la
/// requête : un Directeur ne peut prévisualiser que SON PROPRE abonnement.
/// </summary>
public record ValidatePromoCodeQuery : IRequest<ValidatePromoCodeResult>
{
    public required string Code { get; init; }
    public required BillingPeriod BillingPeriod { get; init; }
}

public record ValidatePromoCodeResult(
    bool IsValid,
    string? Message,
    decimal? BaseAmount,
    decimal? DiscountedAmount,
    bool RequiresPayment);

public class ValidatePromoCodeQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    SubscriptionAmountResolver amountResolver,
    TimeProvider timeProvider)
    : IRequestHandler<ValidatePromoCodeQuery, ValidatePromoCodeResult>
{
    public async Task<ValidatePromoCodeResult> Handle(
        ValidatePromoCodeQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Aucun abonnement associé à votre établissement.");

        var code = request.Code.Trim().ToUpperInvariant();

        var promoCode = await dbContext.PromoCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Code == code, cancellationToken);

        if (promoCode is null)
        {
            return new ValidatePromoCodeResult(false, "Ce code promo n'existe pas.", null, null, true);
        }

        var baseAmount = (await amountResolver.ResolveAsync(schoolId, subscription.Plan, request.BillingPeriod, cancellationToken)).Amount;
        var evaluation = PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount, timeProvider.GetUtcNow());

        if (!evaluation.IsValid)
        {
            return new ValidatePromoCodeResult(false, evaluation.ErrorMessage, baseAmount, null, true);
        }

        return new ValidatePromoCodeResult(
            true, null, baseAmount, evaluation.DiscountedAmount, evaluation.RequiresPayment);
    }
}
