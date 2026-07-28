using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Abonnement d'un établissement. Table de facturation pilotée par le Super Admin, mais rattachée à
/// UNE école et lue en permanence par les utilisateurs de cette école (SubscriptionAwaitingPaymentMiddleware,
/// FeatureAuthorizationHandler, tableau de bord Directeur) : c'est donc une table TENANT à part entière.
///
/// `subscriptions` figure dans les TenantTables de la migration EnableRowLevelSecurity depuis l'origine
/// — la policy RLS PostgreSQL était donc en place, mais le Global Query Filter EF Core manquait.
/// ITenantEntity rétablit les DEUX protections exigées par AGENTS.md règle #2 (« les deux, jamais un
/// seul ») : le filtre est posé automatiquement par ApplicationDbContext.OnModelCreating.
///
/// Les écritures du Super Admin (provisionnement, accès gracieux, confirmation de paiement) ne passent
/// pas par EF : elles empruntent des fonctions SECURITY DEFINER (grant_complimentary_subscription,
/// provision_school_subscription, confirm_subscription_payment) précisément parce que la RLS s'applique
/// et qu'il n'a aucun SchoolId de session à présenter — voir SubscriptionAdminStore.
/// </summary>
public class Subscription : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public SubscriptionPlan Plan { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    /// <summary>Dernier code promo bénéficié (traçabilité/reporting) — ne pilote aucune reconduction automatique.</summary>
    public Guid? PromoCodeId { get; set; }

    /// <summary>
    /// Date jusqu'à laquelle la réduction du <see cref="PromoCodeId"/> a été appliquée. Informatif
    /// uniquement : le tarif plein reprend de lui-même au prochain paiement, calculé à la volée
    /// (aucun job de fond ne repasse ce champ).
    /// </summary>
    public DateOnly? PromoDiscountEndsAt { get; set; }
}