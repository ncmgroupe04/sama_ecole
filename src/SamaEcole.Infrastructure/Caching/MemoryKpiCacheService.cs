using Microsoft.Extensions.Caching.Memory;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Caching;

public class MemoryKpiCacheService(
    IMemoryCache memoryCache,
    ITenantCacheKeyFactory tenantCacheKeyFactory,
    KpiCacheSettings settings) : IKpiCacheService
{
    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
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
