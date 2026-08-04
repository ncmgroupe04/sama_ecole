namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Cache en mémoire des agrégats KPI (dashboards Finance/Directeur), scopé par établissement via
/// ITenantCacheKeyFactory. Interface côté Application pour qu'un futur remplacement par Redis
/// (IDistributedCache, prévu dès la V1 — voir ITenantCacheKeyFactory) ne touche aucun handler.
/// </summary>
public interface IKpiCacheService
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken);

    /// <summary>Invalide une entrée précise pour l'école courante (ex. après un paiement, une présence).</summary>
    void Invalidate(string key);
}

/// <summary>Clés stables des dashboards mis en cache — jamais composées à la main dans un handler.</summary>
public static class KpiCacheKeys
{
    public const string FinanceDashboard = "finance-dashboard";
    public const string DirectorDashboard = "director-dashboard";
}
