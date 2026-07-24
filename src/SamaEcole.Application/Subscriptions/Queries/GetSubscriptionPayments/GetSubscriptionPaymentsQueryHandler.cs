using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;

namespace SamaEcole.Application.Subscriptions.Queries.GetSubscriptionPayments;

public class GetSubscriptionPaymentsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetSubscriptionPaymentsQuery, PaginatedSubscriptionPayments>
{
    public async Task<PaginatedSubscriptionPayments> Handle(
        GetSubscriptionPaymentsQuery request, CancellationToken cancellationToken)
    {
        // Le rapprochement request.SchoolId vs claim JWT est fait EN AMONT par SubscriptionsController
        // via la policy resource-based CanAccessSchoolResource (audit BOLA/IDOR, voir
        // SchoolResourceAuthorizationHandler, même mécanique que InitiateSubscriptionPaymentHandler).
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Subscription n'implémente pas ITenantEntity (voir son commentaire de classe) : filtre manuel,
        // même idiome que InitiateSubscriptionPaymentHandler.
        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);

        if (subscription == null)
        {
            return new PaginatedSubscriptionPayments(
                new List<SubscriptionPaymentListItem>(), 0, request.Page, request.PageSize,
                SamaEcole.Domain.Enums.SubscriptionStatus.AwaitingPayment,
                SamaEcole.Domain.Enums.SubscriptionPlan.Standard,
                null);
        }

        // SubscriptionPayment, lui, EST une ITenantEntity : Global Query Filter + policy RLS cantonnent
        // déjà la lecture à l'école du JWT (même raisonnement que GetAuditLogsQueryHandler).
        var query = dbContext.SubscriptionPayments.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.InitiatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new SubscriptionPaymentListItem(
                p.Id, p.InitiatedAt, p.ConfirmedAt, p.Amount, p.Currency, p.BillingPeriod, p.Method,
                p.Status, p.ProviderTransactionRef))
            .ToListAsync(cancellationToken);

        return new PaginatedSubscriptionPayments(
            items, totalCount, request.Page, request.PageSize,
            subscription.Status, subscription.Plan, subscription.ExpiresAt);
    }
}
