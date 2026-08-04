using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Platform.Queries.GetPlatformDashboard;

/// <summary>
/// GET /admin/platform/dashboard — réservé au Super Admin (console plateforme). Agrège
/// schools/users/subscriptions/subscription_payments de TOUTES les écoles, ce qu'une requête EF Core
/// normale ne pourrait jamais faire pour ce rôle (aucun SchoolId propre, RLS fermée partout,
/// AGENTS.md règle #2) — voir la migration AddPlatformAdminViews pour le mécanisme de contournement.
/// </summary>
public record GetPlatformDashboardQuery : IRequest<PlatformDashboardStatsDto>;

public record PlatformDashboardStatsDto(
    int TotalSchools,
    int TotalUsers,
    decimal TotalRevenue,
    int ActiveSubscriptions,
    decimal MRR,
    decimal ARR,
    decimal ForecastedRevenue30Days,
    decimal ARPU);

public class GetPlatformDashboardQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPlatformDashboardQuery, PlatformDashboardStatsDto>
{
    public async Task<PlatformDashboardStatsDto> Handle(
        GetPlatformDashboardQuery request,
        CancellationToken cancellationToken)
    {
        // La vue `v_platform_dashboard_stats` ne renvoie qu'une seule ligne d'agrégats — jamais
        // filtrée par tenant (elle n'a d'ailleurs aucune colonne SchoolId).
        var stats = await dbContext.PlatformDashboardStats
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var arr = stats.MRR * 12;
        var arpu = stats.ActiveSchools > 0 ? stats.MRR / stats.ActiveSchools : 0;

        return new PlatformDashboardStatsDto(
            stats.TotalSchools, stats.TotalUsers, stats.TotalRevenue, stats.ActiveSubscriptions,
            stats.MRR, arr, stats.ForecastedRevenue30Days, arpu);
    }
}
