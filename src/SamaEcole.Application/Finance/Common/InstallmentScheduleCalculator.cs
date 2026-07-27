using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Common;

/// <summary>
/// Calcule l'état (payé/partiel/en retard/à venir) de chaque échéance d'une inscription, en allouant
/// le cumul <see cref="Enrollment.AmountPaid"/> ÉCHÉANCE PAR ÉCHÉANCE, dans l'ordre — un versement
/// solde d'abord la plus ancienne échéance, jamais une répartition proportionnelle. Extrait de
/// GetStudentBalanceQuery et GetDuesNoticeQuery, qui recopiaient la même boucle deux fois (Étape 5).
///
/// Deux sources d'échéances, mutuellement exclusives pour une inscription donnée :
///   - un <see cref="FeeInstallmentPlan"/> ACTIF (échéancier négocié, Étape 5) — priorité absolue ;
///   - à défaut, la synthèse mensuelle uniforme dérivée d'<see cref="EnrollmentFeeLine"/> ×
///     TuitionMonthsPerYear (comportement historique, INCHANGÉ pour ne régresser aucune inscription
///     existante qui n'a jamais eu d'échéancier personnalisé).
/// </summary>
public static class InstallmentScheduleCalculator
{
    public enum InstallmentState
    {
        Paid,
        Partial,
        Overdue,
        Pending
    }

    public record CalculatedInstallment(
        string Id,
        string Label,
        decimal Amount,
        decimal AmountPaid,
        decimal RemainingDue,
        DateOnly DueDate,
        InstallmentState Status);

    /// <param name="customInstallments">
    /// Échéances du plan ACTIF de l'inscription, DÉJÀ TRIÉES par SequenceNo — null ou vide si aucun
    /// plan personnalisé n'existe, auquel cas la synthèse dérivée des lignes de frais est utilisée.
    /// </param>
    /// <param name="feeLines">
    /// Lignes de frais figées de l'inscription (EnrollmentFeeLine), utilisées uniquement en l'absence
    /// de plan personnalisé — même ordre que GetStudentBalanceQuery/GetDuesNoticeQuery imposaient déjà
    /// (IsRecurring puis Designation), la fonction ne retrie pas elle-même.
    /// </param>
    public static IReadOnlyList<CalculatedInstallment> Calculate(
        decimal totalAmountPaid,
        DateOnly today,
        IReadOnlyList<EnrollmentFeeLine> feeLines,
        DateOnly schoolYearStart,
        IReadOnlyList<FeeInstallment>? customInstallments)
    {
        var remainingPaid = totalAmountPaid;
        var result = new List<CalculatedInstallment>();

        if (customInstallments is { Count: > 0 })
        {
            foreach (var installment in customInstallments.OrderBy(i => i.SequenceNo))
            {
                Allocate($"PLAN-{installment.Id}", installment.Label, installment.Amount, installment.DueDate);
            }

            return result;
        }

        foreach (var line in feeLines)
        {
            if (!line.IsRecurring || line.Months <= 1)
            {
                Allocate($"LINE-{line.Id}", line.Designation, line.LineTotal, schoolYearStart);
            }
            else
            {
                for (var m = 1; m <= line.Months; m++)
                {
                    Allocate(
                        $"MONTH-{line.Id}-{m}",
                        $"{line.Designation} (Mois {m})",
                        line.UnitAmount,
                        schoolYearStart.AddMonths(m - 1));
                }
            }
        }

        return result;

        void Allocate(string id, string label, decimal amount, DateOnly dueDate)
        {
            var paidForThis = Math.Min(remainingPaid, amount);
            remainingPaid -= paidForThis;
            var remainingDue = amount - paidForThis;

            var status = remainingDue <= 0
                ? InstallmentState.Paid
                : paidForThis > 0
                    ? InstallmentState.Partial
                    : dueDate < today
                        ? InstallmentState.Overdue
                        : InstallmentState.Pending;

            result.Add(new CalculatedInstallment(id, label, amount, paidForThis, remainingDue, dueDate, status));
        }
    }
}
