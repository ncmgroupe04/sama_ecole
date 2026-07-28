using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Modification d'un abonnement EXISTANT par le Super Admin (module Tarification &amp; Promotions —
/// attribution manuelle d'un accès offert). Même problème que ISchoolProvisioningStore :
/// `subscriptions` est sous policy RLS (migration AddSubscriptionProvisioning), et un Super Admin
/// sans SchoolId ne satisfait le WITH CHECK d'aucune ligne — un simple UPDATE EF serait rejeté.
/// L'implémentation passe donc, elle aussi, par une fonction SECURITY DEFINER.
/// </summary>
public interface ISubscriptionAdminStore
{
    /// <summary>
    /// Bascule l'abonnement de <paramref name="schoolId"/> sur <paramref name="plan"/>, Actif, avec
    /// l'échéance donnée. Renvoie false si cette école n'a AUCUN abonnement (rien à modifier) — 404
    /// côté Handler, jamais une création implicite ici (voir CreateSchoolCommand pour l'amorçage).
    /// </summary>
    Task<bool> GrantComplimentaryAccessAsync(
        Guid schoolId,
        SubscriptionPlan plan,
        DateOnly expiresAt,
        Guid? promoCodeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ticket JGK-B03 — passage automatique en lecture seule à expiration. Bascule TOUS les abonnements
    /// Actifs dont l'échéance est dépassée sur <see cref="SubscriptionStatus.ReadOnly"/>, toutes écoles
    /// confondues (SubscriptionLifecycleHostedService, hors requête HTTP, ne porte aucun tenant).
    /// Renvoie les <c>SchoolId</c> effectivement basculés, pour notification du Directeur.
    /// </summary>
    Task<IReadOnlyList<Guid>> ExpireOverdueSubscriptionsAsync(DateOnly asOf, CancellationToken cancellationToken);
}
