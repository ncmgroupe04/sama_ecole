using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.IntegrationTests.Common;

/// <summary>Compte courant simulé pour les Handlers qui lisent <see cref="ICurrentUserService"/>.</summary>
public sealed class TestCurrentUser(Guid? userId = null, Role? role = null) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => role;
    public string? IpAddress => null;
}
