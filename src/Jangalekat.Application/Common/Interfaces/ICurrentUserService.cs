using Jangalekat.Domain.Enums;

namespace Jangalekat.Application.Common.Interfaces;

/// <summary>Utilisateur authentifié courant, dérivé du JWT. Implémenté dans Jangalekat.Infrastructure.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Role? Role { get; }
}
