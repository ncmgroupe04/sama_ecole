using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Multitenancy;

public class TenantCacheKeyFactory(ITenantProvider tenantProvider) : ITenantCacheKeyFactory
{
    public string BuildKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return $"school:{schoolId}:{key}";
    }
}
