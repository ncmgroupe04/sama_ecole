using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace SamaEcole.Infrastructure.Multitenancy;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst("sub");
            return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    public Role? Role
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst("role");
            return claim is not null && Enum.TryParse<Role>(claim.Value, out var role) ? role : null;
        }
    }
}
