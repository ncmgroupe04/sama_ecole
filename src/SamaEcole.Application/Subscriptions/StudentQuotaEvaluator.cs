using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Règle de quota d'élèves, SANS accès base de données — pour qu'elle se teste exhaustivement.
///
///   effectif ≤ plafond (Max)       → WithinQuota : on crée.
///   Max &lt; effectif ≤ Soft cap    → InTolerance : on crée, mais on avertit.
///   effectif &gt; Soft cap          → Exceeded    : on refuse.
///
/// Un élève de plus est admis tant que l'effectif APRÈS création reste ≤ Soft cap. Aucune création du
/// tout si la souscription n'est pas <see cref="TenantSubscriptionStatus.Active"/> (en particulier tant
/// que l'Onboarding n'est pas fait).
/// </summary>
public static class StudentQuotaEvaluator
{
    public static StudentQuotaStatus Evaluate(
        int currentStudentCount, int maxStudentLimit, int softQuotaLimit, TenantSubscriptionStatus? status)
    {
        var state = currentStudentCount <= maxStudentLimit ? StudentQuotaState.WithinQuota
            : currentStudentCount <= softQuotaLimit ? StudentQuotaState.InTolerance
            : StudentQuotaState.Exceeded;

        if (status != TenantSubscriptionStatus.Active)
        {
            return new StudentQuotaStatus(
                currentStudentCount, maxStudentLimit, softQuotaLimit, state, false, StudentAdmissionDenial.SubscriptionNotActive);
        }

        // long : le plafond « illimité » vaut int.MaxValue, et int.MaxValue + 1 ne doit jamais déborder.
        var canAdd = (long)currentStudentCount + 1 <= softQuotaLimit;

        return new StudentQuotaStatus(
            currentStudentCount, maxStudentLimit, softQuotaLimit, state, canAdd,
            canAdd ? StudentAdmissionDenial.None : StudentAdmissionDenial.QuotaExceeded);
    }

    /// <summary>Verdict quand l'école n'a aucune souscription : tout est refusé, plafonds à zéro.</summary>
    public static StudentQuotaStatus WithoutSubscription(int currentStudentCount) =>
        new(currentStudentCount, 0, 0, StudentQuotaState.Exceeded, false, StudentAdmissionDenial.SubscriptionNotActive);
}
