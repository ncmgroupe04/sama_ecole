using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Lectures et décisions du SUPER ADMIN sur les demandes de changement d'abonnement.
///
/// Pourquoi un contrat à part : <c>subscription_change_requests</c>, <c>tenant_subscriptions</c> et <c>school_settings</c>
/// sont sous policy RLS, et un Super Admin n'a par définition AUCUN SchoolId de session — un accès EF classique ne verrait
/// ou n'écrirait rien. L'implémentation passe par des fonctions PostgreSQL SECURITY DEFINER qui ne font que ce que l'écran
/// du Super Admin demande (même mécanique que ITenantSubscriptionAdminStore).
/// </summary>
public interface ISubscriptionChangeAdminStore
{
    /// <summary>Les demandes de toutes les écoles, la plus récente d'abord ; filtrées par statut si fourni.</summary>
    Task<IReadOnlyList<SubscriptionChangeRequest>> ListAsync(SubscriptionChangeStatus? status, CancellationToken cancellationToken);

    Task<SubscriptionChangeRequest?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Décide une demande EN ATTENTE. Approuver écrit l'état cible de la demande sur la souscription et les réglages de
    /// l'école, dans la transaction courante ; rejeter ne touche que la demande. Renvoie l'école concernée, ou <c>null</c>
    /// si la demande n'existe pas ou n'est plus en attente.
    ///
    /// Lève <see cref="Exceptions.BusinessRuleException"/> (409) si l'état de l'école a changé depuis la demande
    /// (<c>STALE_CHANGE_REQUEST</c>), si l'effectif dépasse la tranche demandée (<c>TARGET_TIER_TOO_SMALL</c>) ou si un cycle
    /// retiré contient encore des classes (<c>CYCLE_HAS_CLASSROOMS</c>).
    /// </summary>
    Task<Guid?> ResolveAsync(Guid id, bool approve, Guid reviewedBy, string? note, CancellationToken cancellationToken);

    /// <summary>Écoles dont l'effectif dépasse le plafond nominal de leur tranche (état réel, relu d'après les élèves vivants).</summary>
    Task<IReadOnlyList<OverQuotaSchoolDto>> ListOverQuotaSchoolsAsync(CancellationToken cancellationToken);
}
