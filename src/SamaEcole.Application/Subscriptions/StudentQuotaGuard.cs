using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Point d'application UNIQUE du quota d'élèves, appelé par chaque commande qui crée un élève
/// (CreateStudent, CreateEnrollment en nouvelle inscription, ImportStudents) : aucune ne réimplémente la
/// règle, aucune ne peut dériver des autres. Un réinscrit n'ajoute personne à l'effectif — il n'y passe pas.
///
/// Le contrôle précède la transaction d'écriture et ne la verrouille pas : deux créations strictement
/// concurrentes à la limite peuvent dépasser la tolérance d'un ou deux élèves. C'est une limite
/// commerciale, pas une invariante d'intégrité — le même compromis que la capacité des chambres
/// (CreateEnrollmentCommandHandler).
/// </summary>
public class StudentQuotaGuard(ITenantSubscriptionService subscriptionService)
{
    /// <summary>
    /// Refuse (<see cref="QuotaExceededException"/>) si <paramref name="additionalStudents"/> élèves de plus
    /// ne sont pas admis. Renvoie un avertissement si l'effectif résultant dépasse le plafond nominal.
    /// </summary>
    public async Task<StudentQuotaWarning?> EnsureCanAddAsync(int additionalStudents, CancellationToken cancellationToken)
    {
        var quota = await subscriptionService.GetQuotaStatusAsync(additionalStudents, cancellationToken);

        if (!quota.CanAddStudent)
        {
            throw quota.Denial == StudentAdmissionDenial.SubscriptionNotActive
                ? new QuotaExceededException(
                    "Votre abonnement n'est pas actif : la création d'élèves est impossible. " +
                    "Terminez la configuration de votre établissement ou contactez le support.",
                    quota, quota.Denial)
                : new QuotaExceededException(
                    additionalStudents == 1
                        ? $"Quota d'élèves atteint ({quota.CurrentStudentCount} élèves pour une limite de " +
                          $"{quota.MaxStudentLimit}, tolérance {quota.SoftQuotaLimit} comprise). Passez à une tranche supérieure pour continuer."
                        : $"Cette opération ajouterait {additionalStudents} élèves : votre quota ({quota.CurrentStudentCount} élèves, " +
                          $"limite {quota.MaxStudentLimit}, tolérance {quota.SoftQuotaLimit} comprise) ne le permet pas. " +
                          "Réduisez le fichier ou passez à une tranche supérieure.",
                    quota, quota.Denial);
        }

        var resulting = (long)quota.CurrentStudentCount + additionalStudents;

        return resulting > quota.MaxStudentLimit
            ? new StudentQuotaWarning(
                (int)resulting, quota.MaxStudentLimit, quota.SoftQuotaLimit,
                (int)Math.Max(0, quota.SoftQuotaLimit - resulting))
            : null;
    }
}
