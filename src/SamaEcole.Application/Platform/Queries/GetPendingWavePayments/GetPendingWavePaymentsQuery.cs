using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Platform.Queries.GetPendingWavePayments;

/// <summary>
/// GET /admin/platform/wave-payments — réservé au Super Admin (écran Abonnements &amp; Facturation) : demandes de
/// paiement d'abonnement par le compte marchand Wave, en attente de rapprochement avec le relevé Wave. Liste
/// complète, non paginée : seules les demandes NON traitées y figurent, leur nombre reste modeste.
/// </summary>
public record GetPendingWavePaymentsQuery : IRequest<IReadOnlyList<PendingWavePayment>>;

public class GetPendingWavePaymentsQueryHandler(ISubscriptionPaymentStore paymentStore)
    : IRequestHandler<GetPendingWavePaymentsQuery, IReadOnlyList<PendingWavePayment>>
{
    public Task<IReadOnlyList<PendingWavePayment>> Handle(
        GetPendingWavePaymentsQuery request, CancellationToken cancellationToken)
        => paymentStore.ListPendingWavePaymentsAsync(cancellationToken);
}
