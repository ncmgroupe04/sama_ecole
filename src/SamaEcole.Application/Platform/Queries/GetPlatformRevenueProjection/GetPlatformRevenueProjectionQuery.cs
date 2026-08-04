using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Platform.Queries.GetPlatformRevenueProjection;

/// <summary>
/// GET /admin/platform/revenue-projection — réservé au Super Admin. Alimente le graphique de
/// projection (12 mois) de l'Executive Dashboard : pour chaque école dont l'abonnement est
/// ACTUELLEMENT Active, projette ses échéances de renouvellement futures (ExpiresAt, puis +N fois
/// la période de facturation de son dernier paiement confirmé) sur les 12 prochains mois, et cumule
/// le montant attendu par mois. Aucune hypothèse de croissance (nouvelles écoles) n'est faite ici —
/// c'est le rôle du Simulateur de Croissance, purement côté client (ARPU × nombre d'écoles cible).
///
/// Contrairement au MRR du dashboard (équivalent mensuel lissé, Yearly / 12), cette courbe reflète
/// le CASH réellement attendu mois par mois : un abonnement Yearly n'apparaît qu'au(x) mois de son
/// échéance, pas chaque mois — c'est délibéré, un « graphique de projection basé sur les échéances »
/// doit montrer les à-coups réels, pas les lisser.
///
/// Une école sans aucun paiement confirmé (LastPaymentAmountXof/LastPaymentBillingPeriod null) est
/// exclue : sans historique, sa cadence de renouvellement n'est pas connaissable, et l'inclure avec
/// une hypothèse arbitraire fausserait la projection plutôt que de simplement l'omettre.
/// </summary>
public record GetPlatformRevenueProjectionQuery : IRequest<IReadOnlyList<MonthlyRevenueProjectionDto>>;

public record MonthlyRevenueProjectionDto(string Month, decimal ProjectedRevenue);

public class GetPlatformRevenueProjectionQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetPlatformRevenueProjectionQuery, IReadOnlyList<MonthlyRevenueProjectionDto>>
{
    private const int HorizonMonths = 12;

    public async Task<IReadOnlyList<MonthlyRevenueProjectionDto>> Handle(
        GetPlatformRevenueProjectionQuery request, CancellationToken cancellationToken)
    {
        var renewals = await dbContext.PlatformSubscriptions
            .AsNoTracking()
            .Where(row => row.Status == "Active"
                && row.ExpiresAt != null
                && row.LastPaymentAmountXof != null
                && row.LastPaymentBillingPeriod != null)
            .Select(row => new
            {
                row.ExpiresAt,
                row.LastPaymentAmountXof,
                row.LastPaymentBillingPeriod
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var firstMonth = new DateOnly(today.Year, today.Month, 1);
        var months = Enumerable.Range(0, HorizonMonths).Select(firstMonth.AddMonths).ToList();
        var horizonEnd = months[^1].AddMonths(1);
        var buckets = months.ToDictionary(m => m, _ => 0m);

        foreach (var renewal in renewals)
        {
            var step = renewal.LastPaymentBillingPeriod switch
            {
                "Monthly" => 1,
                "Yearly" => 12,
                _ => (int?)null
            };
            if (step is null)
            {
                continue;
            }

            // Avance la première échéance connue jusqu'à la prochaine échéance FUTURE : un abonnement
            // dont ExpiresAt est déjà passé (renouvellement en retard) doit quand même apparaître à sa
            // prochaine échéance projetée, pas disparaître silencieusement de la courbe.
            var dueDate = renewal.ExpiresAt!.Value;
            while (dueDate < today)
            {
                dueDate = dueDate.AddMonths(step.Value);
            }

            while (dueDate < horizonEnd)
            {
                var bucketKey = new DateOnly(dueDate.Year, dueDate.Month, 1);
                if (buckets.ContainsKey(bucketKey))
                {
                    buckets[bucketKey] += renewal.LastPaymentAmountXof!.Value;
                }
                dueDate = dueDate.AddMonths(step.Value);
            }
        }

        return months
            .Select(m => new MonthlyRevenueProjectionDto(m.ToString("yyyy-MM"), buckets[m]))
            .ToList();
    }
}
