using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
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
    TimeProvider timeProvider)
    : IRequestHandler<InitiateSubscriptionPaymentCommand, InitiateSubscriptionPaymentResult>
{
    public async Task<InitiateSubscriptionPaymentResult> Handle(
        InitiateSubscriptionPaymentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le SchoolId de l'URL n'est JAMAIS la source de vérité (règle #10) : s'il diverge du tenant
        // réel de la session, mieux vaut un refus explicite qu'une réussite silencieuse sur la mauvaise
        // ressource — même si RLS empêcherait de toute façon toute fuite de données d'une autre école.
        if (request.SchoolId != schoolId)
        {
            throw new UnauthorizedAccessException("L'établissement de l'URL ne correspond pas à votre session.");
        }

        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Aucun abonnement associé à votre établissement.");

        var school = await dbContext.Schools
            .AsNoTracking()
            .SingleAsync(s => s.Id == schoolId, cancellationToken);

        var director = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(u => u.Id == currentUser.UserId, cancellationToken);

        var amount = pricingProvider.GetAmount(subscription.Plan, request.BillingPeriod);
        var paymentId = Guid.CreateVersion7();

        var description =
            $"Abonnement Sama Ecole — {school.Name} — {subscription.Plan} "
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
        await dbContext.SaveChangesAsync(cancellationToken);

        return new InitiateSubscriptionPaymentResult(payment.Id, initiation.RedirectUrl, payment.Status);
    }
}
