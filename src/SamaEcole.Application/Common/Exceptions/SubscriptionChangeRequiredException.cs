using SamaEcole.Application.Subscriptions.Changes;

namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Une modification directe des paramètres (profil, cycles, module non souscrit) a été INTERCEPTÉE : elle change
/// l'abonnement de l'école, donc elle passe par une demande que le Super Admin valide. Rien n'a été écrit.
///
/// Traduite en HTTP 409 (<c>SUBSCRIPTION_CHANGE_REQUIRED</c>) par <c>ExceptionHandlingMiddleware</c>, avec l'aperçu
/// (<see cref="Preview"/>) en <c>details</c> : ce qui changerait, le tarif ajusté, et la demande à envoyer telle quelle.
/// L'interface (modale universelle) l'affiche sans second appel.
///
/// 409 et non 422 : la saisie est valide, c'est l'état de l'abonnement qui s'oppose à l'écriture directe.
/// </summary>
public class SubscriptionChangeRequiredException(SubscriptionChangePreview preview) : Exception(preview.Message)
{
    public SubscriptionChangePreview Preview { get; } = preview;
}
