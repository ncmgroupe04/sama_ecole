using SamaEcole.Application.Subscriptions;

namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Refus de créer un ou plusieurs élèves : le quota de la souscription est atteint, ou la souscription
/// n'est pas <c>Active</c> (Onboarding à faire, suspendue, expirée). Traduite en HTTP 422
/// (<c>STUDENT_QUOTA_EXCEEDED</c> / <c>SUBSCRIPTION_NOT_ACTIVE</c>) par <c>ExceptionHandlingMiddleware</c>,
/// avec <see cref="Quota"/> en <c>details</c> pour que l'écran affiche les chiffres.
///
/// 422 et non 409 : c'est la règle commerciale du plan de l'école qui refuse, pas l'état d'une ressource
/// ni une écriture concurrente — et le Directeur peut y remédier en changeant de tranche.
/// </summary>
public class QuotaExceededException(string message, StudentQuotaStatus quota, StudentAdmissionDenial denial)
    : Exception(message)
{
    public StudentQuotaStatus Quota { get; } = quota;

    public StudentAdmissionDenial Denial { get; } = denial;
}
