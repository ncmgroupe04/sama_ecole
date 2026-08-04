using Microsoft.Extensions.Caching.Memory;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Caching;

public class MemoryKpiCacheService(
    IMemoryCache memoryCache,
    ITenantCacheKeyFactory tenantCacheKeyFactory,
    ITenantProvider tenantProvider,
    KpiCacheSettings settings) : IKpiCacheService
{
    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        // Un Super Admin (ReportsController.Dashboard autorise SuperAdmin) n'a pas de SchoolId : c'est
        // légitime, pas une erreur — il n'a pas d'établissement. ITenantCacheKeyFactory.BuildKey est
        // délibérément fail-closed pour toute AUTRE donnée tenant (lève UnauthorizedAccessException sans
        // SchoolId), et ce comportement reste correct pour ces consommateurs-là. Mais un dashboard KPI
        // est un cas différent : l'endpoint est un 200 légitime avec agrégats vides (Global Query Filter
        // + RLS ferment déjà toutes les tables tenant), donc on calcule à la volée SANS jamais construire
        // de clé de cache "globale" — même traitement que le cache désactivé, jamais un appel à BuildKey.
        if (!settings.Enabled || tenantProvider.CurrentSchoolId is null)
        {
            return await factory(cancellationToken);
        }

        var fullKey = tenantCacheKeyFactory.BuildKey(key);

        if (memoryCache.TryGetValue(fullKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        memoryCache.Set(fullKey, value, TimeSpan.FromMinutes(settings.TtlMinutes));
        return value;
    }

    public void Invalidate(string key) => memoryCache.Remove(tenantCacheKeyFactory.BuildKey(key));
}
