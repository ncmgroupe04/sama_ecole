using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Platform.Queries.GetPlatformSubscriptions;

/// <summary>
/// GET /admin/platform/subscriptions — réservé au Super Admin (console plateforme, écran Abonnements
/// &amp; Facturation). Une ligne par école ayant un abonnement, avec son dernier paiement CONFIRMÉ le
/// cas échéant. Lit la vue `v_platform_subscriptions` (migration AddPlatformSubscriptionsAndImpersonation),
/// même mécanisme de contournement RLS que GetPlatformDashboardQuery. Liste complète, non paginée,
/// comme GET /schools (nombre d'écoles modeste, filtrage/recherche côté client).
/// </summary>
public record GetPlatformSubscriptionsQuery : IRequest<IReadOnlyList<PlatformSubscriptionDto>>;

public record PlatformSubscriptionDto(
    Guid SchoolId,
    string SchoolName,
    string Plan,
    string Status,
    DateOnly? ExpiresAt,
    decimal? LastPaymentAmountXof,
    DateTimeOffset? LastPaymentAt);

public class GetPlatformSubscriptionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPlatformSubscriptionsQuery, IReadOnlyList<PlatformSubscriptionDto>>
{
    public async Task<IReadOnlyList<PlatformSubscriptionDto>> Handle(
        GetPlatformSubscriptionsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.PlatformSubscriptions
            .AsNoTracking()
            .OrderBy(row => row.SchoolName)
            .Select(row => new PlatformSubscriptionDto(
                row.SchoolId, row.SchoolName, row.Plan, row.Status,
                row.ExpiresAt, row.LastPaymentAmountXof, row.LastPaymentAt))
            .ToListAsync(cancellationToken);
    }
}
