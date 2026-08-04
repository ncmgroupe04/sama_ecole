using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subscriptions.Commands.InitiateSubscriptionPayment;

/// <summary>
/// Ticket JGK-I05. Le montant est TOUJOURS calculé serveur (plan de l'abonnement × période choisie via
/// ISubscriptionPricingProvider) — jamais fourni par le client, même indirectement. L'agrégateur est
/// appelé AVANT toute écriture locale : si PayDunya (ou tout IPaymentService) refuse, aucune ligne
/// SubscriptionPayment ne doit exister pour une facture qui n'a jamais été créée côté agrégateur.
///
/// <see cref="SubscriptionPayment.Id"/> est généré ICI, avant l'appel externe, et transmis à
/// l'agrégateur en donnée personnalisée (voir PaymentInitiationRequest.InternalPaymentId) : c'est ce qui
/// « lie la transaction de paiement à la référence de l'abonnement » (critère du ticket) de façon
/// redondante avec ProviderTransactionRef, en préparation du traitement du callback (JGK-I06).
/// </summary>
public class InitiateSubscriptionPaymentHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    IPaymentService paymentService,
    ISubscriptionPricingProvider pricingProvider,
    IAuditLogStore auditLogStore,
    TimeProvider timeProvider)
    : IRequestHandler<InitiateSubscriptionPaymentCommand, InitiateSubscriptionPaymentResult>
{
    public async Task<InitiateSubscriptionPaymentResult> Handle(
        InitiateSubscriptionPaymentCommand request, CancellationToken cancellationToken)
    {
        // Le SchoolId de l'URL n'est JAMAIS la source de vérité (règle #10). Le rapprochement avec le
        // tenant réel de la session (request.SchoolId vs claim JWT) est désormais fait EN AMONT, par
        // SubscriptionsController via la policy resource-based CanAccessSchoolResource (audit BOLA/IDOR,
        // voir SchoolResourceAuthorizationHandler) — ce Handler ne peut donc être atteint qu'avec un
        // SchoolId déjà vérifié.
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Aucun abonnement associé à votre établissement.");

        var school = await dbContext.Schools
            .AsNoTracking()
            .SingleAsync(s => s.Id == schoolId, cancellationToken);

        var director = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(u => u.Id == currentUser.UserId, cancellationToken);

        var baseAmount = pricingProvider.GetAmount(subscription.Plan, request.BillingPeriod);

        // Module Tarification & Promotions — RE-VALIDATION SERVEUR INTÉGRALE (règle #10) : jamais le
        // montant prévisualisé par ValidatePromoCodeQuery, qui n'a d'ailleurs rien réservé (lecture
        // seule). PromoCode chargé EN SUIVI : CurrentUses sera incrémenté plus bas, sous verrou xmin.
        PromoCode? promoCode = null;
        var amount = baseAmount;
        var requiresPayment = true;

        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var normalizedCode = request.PromoCode.Trim().ToUpperInvariant();

            promoCode = await dbContext.PromoCodes
                .SingleOrDefaultAsync(p => p.Code == normalizedCode, cancellationToken);

            var evaluation = promoCode is null
                ? PromoCodeEvaluation.Invalid("Ce code promo n'existe pas.")
                : PromoCodeDiscountCalculator.Evaluate(promoCode, baseAmount, timeProvider.GetUtcNow());

            if (!evaluation.IsValid)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.PromoCode), evaluation.ErrorMessage)
                ]);
            }

            amount = evaluation.DiscountedAmount;
            requiresPayment = evaluation.RequiresPayment;
        }

        // FreeTrialMonths/FullDiscount : aucun argent ne change de main, donc AUCUN appel à
        // l'agrégateur et AUCUN SubscriptionPayment — créer une ligne à 0 FCFA "confirmée" sans
        // webhook violerait l'esprit de la règle #11. L'abonnement passe directement Actif.
        if (!requiresPayment)
        {
            return await dbContext.ExecuteInTransactionAsync(async ct =>
            {
                RedeemPromoCode(promoCode!, subscription, timeProvider);
                await dbContext.SaveChangesAsync(ct);

                await auditLogStore.AppendAsync(
                    schoolId, currentUser.UserId!.Value, "Subscriptions", "RedeemPromoCode",
                    success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), ct);

                return new InitiateSubscriptionPaymentResult(
                    PaymentId: null, RedirectUrl: null, Status: null, ActivatedWithoutPayment: true);
            }, cancellationToken);
        }

        var paymentId = Guid.CreateVersion7();

        var description =
            $"Abonnement Unikol — {school.Name} — {subscription.Plan} "
            + $"({(request.BillingPeriod == Domain.Enums.BillingPeriod.Monthly ? "mensuel" : "annuel")})";

        var initiation = await paymentService.InitiatePaymentAsync(
            new PaymentInitiationRequest(paymentId, schoolId, amount, "XOF", description, director.FullName, director.Email),
            cancellationToken);

        var payment = new SubscriptionPayment
        {
            Id = paymentId,
            SchoolId = schoolId,
            SubscriptionId = subscription.Id,
            Amount = amount,
            Currency = "XOF",
            Method = request.Method,
            BillingPeriod = request.BillingPeriod,
            Provider = paymentService.ProviderName,
            ProviderTransactionRef = initiation.ProviderTransactionRef,
            InitiatedAt = timeProvider.GetUtcNow()
        };

        dbContext.SubscriptionPayments.Add(payment);

        if (promoCode is not null)
        {
            RedeemPromoCode(promoCode, subscription, timeProvider);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new InitiateSubscriptionPaymentResult(payment.Id, initiation.RedirectUrl, payment.Status);
    }

    /// <summary>
    /// Incrémente CurrentUses (protégé par le verrou optimiste xmin de PromoCodeConfiguration — une
    /// course concurrente sur MaxUses se traduit en ConcurrencyConflictException/409, jamais un
    /// dépassement silencieux) et trace le bénéfice sur l'abonnement — Subscription appartient déjà au
    /// tenant courant, un UPDATE EF normal satisfait la policy RLS (WITH CHECK sur SchoolId).
    /// </summary>
    private static void RedeemPromoCode(PromoCode promoCode, Subscription subscription, TimeProvider timeProvider)
    {
        promoCode.CurrentUses++;

        subscription.PromoCodeId = promoCode.Id;

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (promoCode.DiscountType is PromoDiscountType.FreeTrialMonths or PromoDiscountType.FullDiscount)
        {
            var expiresAt = today.AddMonths(promoCode.DurationMonths!.Value);
            subscription.Status = SubscriptionStatus.Active;
            subscription.ExpiresAt = expiresAt;
            subscription.PromoDiscountEndsAt = expiresAt;
        }
        else
        {
            // Percentage/FixedAmount : n'étend PAS ExpiresAt (c'est la confirmation du paiement,
            // JGK-I06, qui le fait) — seule la fenêtre de reconduction du RABAIS est tracée ici, à
            // titre informatif (aucun job de fond ne s'y adosse, voir PromoCodeDiscountCalculator).
            subscription.PromoDiscountEndsAt = promoCode.DurationMonths is { } months
                ? today.AddMonths(months)
                : null;
        }
    }
}
