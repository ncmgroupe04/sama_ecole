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

    /// <summary>
    /// Crée la souscription commerciale initiale (<c>tenant_subscriptions</c>) en <c>PendingOnboarding</c> :
    /// l'école devra choisir son profil et sa tranche avant tout usage. Même problème RLS que les deux
    /// méthodes ci-dessus, même fonction SECURITY DEFINER gardée. Renvoie null si l'école a DÉJÀ une
    /// souscription vivante.
    /// </summary>
    Task<Guid?> CreateInitialTenantSubscriptionAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Crée la souscription commerciale d'une école neuve DÉJÀ CONFIGURÉE : <c>Active</c> d'emblée, avec le profil, la
    /// tranche et les modules choisis à l'inscription, et aligne les réglages de l'école (<c>school_settings</c>) sur
    /// ces mêmes choix — le Directeur n'a donc pas d'Onboarding à faire. Même problème RLS que les méthodes
    /// ci-dessus (le Super Admin n'a aucun schoolId de session), même fonction SECURITY DEFINER gardée : elle ne
    /// touche qu'une école SANS souscription et sans profil déjà choisi. Renvoie null sinon.
    /// </summary>
    Task<Guid?> CreateConfiguredTenantSubscriptionAsync(
        Guid schoolId, ConfiguredTenantSubscription configuration, CancellationToken cancellationToken);

    /// <summary>Un e-mail identifie un compte sur TOUTE la plateforme (docs/Volume_3_DDS.md §5.2).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);
}

/// <summary>
/// Ce que l'approbation d'une offre par tranche écrit sur la souscription commerciale ET sur les réglages de l'école.
/// Plafonds (<paramref name="MaxStudentLimit"/>, <paramref name="SoftQuotaLimit"/>) calculés par l'appelant d'après la
/// tranche (StudentQuotaDefaults) — la base ne les recalcule pas.
/// </summary>
public sealed record ConfiguredTenantSubscription(
    ProfileType Profile,
    ProfileEtablissement EstablishmentProfile,
    StudentQuotaTier Tier,
    int MaxStudentLimit,
    int SoftQuotaLimit,
    bool IsPedagogyEnabled,
    bool IsFinanceEnabled,
    bool IsInternatEnabled,
    bool IsCoranModuleEnabled,
    string ManagedCycles,
    bool HasBoarding);
