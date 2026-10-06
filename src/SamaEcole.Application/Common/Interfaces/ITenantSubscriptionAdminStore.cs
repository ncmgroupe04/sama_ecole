using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Lecture et modification de la souscription commerciale de N'IMPORTE QUELLE école, par le Super Admin.
/// Même problème que ISubscriptionAdminStore : <c>tenant_subscriptions</c> est sous RLS et un Super Admin
/// n'a aucun SchoolId de session — l'implémentation passe par des fonctions SECURITY DEFINER
/// (<c>get_tenant_subscription</c>, <c>update_tenant_subscription</c>), jamais par EF.
///
/// À ne JAMAIS appeler pour l'école courante d'un Directeur : celle-là se lit par
/// <see cref="ITenantSubscriptionService"/>, sous RLS.
/// </summary>
public interface ITenantSubscriptionAdminStore
{
    /// <summary>La souscription vivante de l'école, ou <c>null</c> si elle n'en a pas.</summary>
    Task<TenantSubscriptionDto?> GetAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Modifie tranche, plafonds et/ou statut ; un paramètre <c>null</c> reste inchangé. Renvoie la ligne
    /// après modification, ou <c>null</c> si l'école n'a aucune souscription vivante (rien n'est créé ici).
    /// </summary>
    Task<TenantSubscriptionDto?> UpdateAsync(
        Guid schoolId,
        StudentQuotaTier? tier,
        int? maxStudentLimit,
        int? softQuotaLimit,
        TenantSubscriptionStatus? status,
        Guid updatedBy,
        CancellationToken cancellationToken);
}
