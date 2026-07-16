using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Utilisateur authentifié courant, dérivé du JWT. Implémenté dans SamaEcole.Infrastructure.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Role? Role { get; }

    /// <summary>Adresse IP de l'appelant (docs/Volume_7_Security.md §7 : champ obligatoire du journal d'audit).</summary>
    string? IpAddress { get; }
}
