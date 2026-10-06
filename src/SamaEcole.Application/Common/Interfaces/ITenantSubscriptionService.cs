using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// LECTURE et VÉRIFICATION de la souscription commerciale de l'établissement COURANT (celui du JWT,
/// jamais un paramètre — AGENTS.md règle #10). Volontairement sans aucune méthode d'écriture : les
/// changements passent par des Commands MediatR (règle #7).
/// </summary>
public interface ITenantSubscriptionService
{
    /// <summary>La souscription de l'école courante, ou <c>null</c> si aucune n'a encore été créée.</summary>
    Task<TenantSubscriptionDto?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Vrai si le module est activé sur la souscription. Faux — jamais une exception — si l'école n'a pas
    /// de souscription : fail-closed.
    /// </summary>
    Task<bool> IsModuleEnabledAsync(SchoolModule module, CancellationToken cancellationToken);

    /// <summary>
    /// Effectif courant, plafonds et verdict « peut-on créer un élève de plus ». Fail-closed : sans
    /// souscription, ou si elle n'est pas <c>Active</c>, la réponse est <c>CanAddStudent = false</c>.
    /// </summary>
    Task<StudentQuotaStatus> GetQuotaStatusAsync(CancellationToken cancellationToken);
}
