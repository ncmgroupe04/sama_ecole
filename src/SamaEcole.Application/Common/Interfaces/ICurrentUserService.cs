using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Utilisateur authentifié courant, dérivé du JWT. Implémenté dans SamaEcole.Infrastructure.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Role? Role { get; }
}
