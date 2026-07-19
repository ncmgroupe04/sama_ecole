using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Accès aux données du chemin d'AUTHENTIFICATION (ticket JGK-A04).
///
/// Pourquoi un contrat séparé d'IApplicationDbContext : le login et le refresh s'exécutent sans
/// tenant (l'appelant n'a pas encore de JWT), or la table users est protégée par une policy RLS sur
/// SchoolId — une requête EF classique n'y verrait donc AUCUNE ligne. L'implémentation passe par des
/// fonctions PostgreSQL SECURITY DEFINER strictement limitées à ces opérations (ticket JGK-A03).
/// </summary>
public interface IAuthStore
{
    Task<AuthUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken);

    Task<AuthUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Console Super Admin (bouton « Infiltrer », « Relancer ») : le compte Directeur actif d'une école
    /// cible, pour émettre un jeton d'impersonation ou envoyer un rappel de paiement. Contourne la RLS
    /// de `users` par la même fonction SECURITY DEFINER que le login (auth_find_active_director_by_school,
    /// migration AddPlatformSubscriptionsAndImpersonation) — jamais une lecture directe de la table.
    /// Renvoie null si l'école n'a aucun Directeur actif (compte suspendu/bloqué/supprimé).
    /// </summary>
    Task<AuthUser?> FindActiveDirectorForSchoolAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>Remet à zéro le compteur d'échecs (succès) ou l'incrémente et verrouille au besoin.</summary>
    Task RecordLoginAttemptAsync(
        Guid userId,
        bool success,
        int maxFailedAttempts,
        int lockoutMinutes,
        CancellationToken cancellationToken);

    Task StoreRefreshTokenAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<StoredRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    Task RevokeRefreshTokenAsync(Guid tokenId, CancellationToken cancellationToken);

    /// <summary>
    /// Révoque tous les refresh tokens actifs de l'utilisateur (logout, rejeu détecté, ou suspension
    /// par le Directeur — ticket JGK-A05). Renvoie le nombre de sessions effectivement coupées.
    /// </summary>
    Task<int> RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken);
}

public record AuthUser(
    Guid Id,
    Guid? SchoolId,
    string Email,
    string PasswordHash,
    string FullName,
    Role Role,
    EntityStatus Status,
    int AccessFailedCount,
    DateTimeOffset? LockoutEndAt);

public record StoredRefreshToken(
    Guid Id,
    Guid UserId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);