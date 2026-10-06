using MediatR;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Subscriptions.Queries.GetTenantSubscription;

/// <summary>
/// GET /onboarding/subscription — la souscription de l'école courante (statut, profil, tranche, plafonds,
/// modules). Lisible PENDANT l'Onboarding : c'est ce qui dit à l'écran s'il doit encore s'afficher.
/// </summary>
public record GetTenantSubscriptionQuery : IRequest<TenantSubscriptionDto>;

public class GetTenantSubscriptionQueryHandler(ITenantSubscriptionService subscriptionService)
    : IRequestHandler<GetTenantSubscriptionQuery, TenantSubscriptionDto>
{
    public async Task<TenantSubscriptionDto> Handle(GetTenantSubscriptionQuery request, CancellationToken cancellationToken) =>
        await subscriptionService.GetCurrentAsync(cancellationToken)
            ?? throw new NotFoundException("Aucune souscription n'est rattachée à votre établissement. Contactez le support.");
}
