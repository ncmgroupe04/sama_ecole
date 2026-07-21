using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Jeton de réinitialisation de mot de passe self-service (POST /auth/forgot-password puis
/// /auth/reset-password, docs/Volume_4_API_Design.md §1).
///
/// Ne porte volontairement PAS de SchoolId et n'est PAS une table tenant — même raisonnement que
/// <see cref="RefreshToken"/>, et pour la même raison : un utilisateur qui a oublié son mot de passe
/// n'est PAS authentifié, il n'y a donc aucun tenant à résoudre. Une policy RLS sur SchoolId rendrait
/// la réinitialisation structurellement impossible. Cette table ne contient d'ailleurs aucune donnée
/// d'établissement — un UserId, un condensat opaque et des dates.
///
/// Le jeton en clair n'est JAMAIS stocké : seul son SHA-256 l'est. Une fuite de la base ne permet donc
/// pas de forger un lien de réinitialisation valide — alors qu'un jeton stocké en clair donnerait à
/// l'attaquant la prise de contrôle immédiate de tous les comptes ayant une demande en cours.
/// </summary>
public class PasswordResetToken : AuditableEntity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 (base64) du jeton envoyé par e-mail. Jamais le jeton lui-même.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Horodatage de CONSOMMATION — usage unique. Un lien de réinitialisation reste dans la boîte mail
    /// bien après avoir servi : sans cette marque, quiconque accède ensuite à cette boîte (poste
    /// partagé, sauvegarde, transfert) pourrait le rejouer indéfiniment.
    /// </summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>
    /// Non nul dès que le jeton est invalidé sans avoir servi : une nouvelle demande périme les
    /// précédentes, et une réinitialisation réussie périme les autres demandes en cours du même compte.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && now < ExpiresAt;
}
