using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Création du TOUT PREMIER compte d'un établissement (ticket JGK-B01).
///
/// Pourquoi un contrat à part, et pas un simple dbContext.Users.Add : la table `users` est sous
/// policy RLS (migration AddAuthentication), et un Super Admin n'a par définition AUCUN schoolId —
/// sa session ne satisfait donc le WITH CHECK d'aucune ligne, et l'INSERT est rejeté
/// (« new row violates row-level security policy »). C'est le comportement voulu : on ne l'affaiblit
/// pas, on ouvre une porte étroite et surveillée.
///
/// L'implémentation passe par une fonction PostgreSQL SECURITY DEFINER qui REFUSE de créer un compte
/// dans un établissement possédant déjà un utilisateur. Conséquence : même détournée, elle ne peut
/// pas injecter un Directeur dans une école existante — donc pas d'escalade vers un autre tenant.
/// </summary>
public interface ISchoolProvisioningStore
{
    /// <summary>
    /// Crée le compte Directeur initial. Renvoie null si l'établissement a DÉJÀ un utilisateur —
    /// il n'est alors plus à provisionner, et cette porte doit rester fermée.
    /// </summary>
    Task<Guid?> CreateInitialDirectorAsync(
        Guid schoolId,
        string email,
        string passwordHash,
        string fullName,
        Role role,
        CancellationToken cancellationToken);

    /// <summary>
    /// Crée l'abonnement initial d'un établissement (ticket JGK-I03). Même problème que le Directeur :
    /// `subscriptions` est sous policy RLS, et un Super Admin sans schoolId ne satisferait le WITH CHECK
    /// d'aucune ligne — d'où une seconde fonction SECURITY DEFINER, avec la même garde anti-escalade
    /// (elle refuse d'agir sur un établissement qui possède DÉJÀ un abonnement). Renvoie null dans ce cas.
    /// </summary>
    Task<Guid?> CreateInitialSubscriptionAsync(
        Guid schoolId,
        SubscriptionPlan plan,
        SubscriptionStatus status,
        CancellationToken cancellationToken);

    /// <summary>Un e-mail identifie un compte sur TOUTE la plateforme (docs/Volume_3_DDS.md §5.2).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);
}
