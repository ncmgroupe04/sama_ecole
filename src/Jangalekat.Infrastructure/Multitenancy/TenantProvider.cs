using Jangalekat.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Jangalekat.Infrastructure.Multitenancy;

/// <summary>
/// Résout le SchoolId exclusivement depuis le claim JWT "schoolId" — jamais depuis la query
/// string, un header custom ou le body (AGENTS.md règle #10). Un Directeur/Enseignant/etc.
/// ne peut donc jamais forger un SchoolId différent du sien.
/// </summary>
public class TenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    public Guid? CurrentSchoolId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst("schoolId");
            return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
        }
    }
}
