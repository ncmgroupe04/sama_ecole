using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Refresh token (ticket JGK-A04).
///
/// Ne porte volontairement PAS de SchoolId et n'est PAS une table tenant : le refresh s'effectue
/// AVANT toute résolution de tenant (l'appelant n'a pas encore de JWT valide), une policy RLS sur
/// SchoolId rendrait donc le renouvellement impossible. Cette table ne contient d'ailleurs aucune
/// donnée métier d'établissement — seulement un condensat opaque et des dates.
///
/// Le token en clair n'est JAMAIS stocké : seul son SHA-256 l'est. Une fuite de la base ne permet
/// donc pas de rejouer une session.
/// </summary>
public class RefreshToken : AuditableEntity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 (base64) du token remis au client. Jamais le token lui-même.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Non nul dès que le token est révoqué (logout, rotation, ou détection de rejeu).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}