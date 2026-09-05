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
        InstallmentState Status,

        /// <summary>
        /// Catégorie de frais d'origine (traçabilité + ventilation du reçu de caisse). Renseignée
        /// pour la synthèse dérivée des lignes de frais ; <c>null</c> pour un échéancier personnalisé
        /// (FeeInstallment ne porte pas de catégorie) — le reçu retombe alors sur sa ligne unique.
        /// </summary>
        Guid? FeeCategoryId = null,

        /// <summary>
        /// Vrai si cette échéance fait partie de l'ENGAGEMENT INITIAL, celui que le secrétariat
        /// annonce sur l'attestation et que le tuteur règle en arrivant à la caisse : chaque frais
        /// ponctuel EN ENTIER + le PREMIER mois de chaque frais récurrent (hypothèse « un mois
        /// d'avance », alignée sur EnrollmentReceiptDto.InitialSettlementTotal et
        /// docs/design-references/README.md §1). Les mois 2..N n'en font pas partie : on n'encaisse
        /// pas d'avance une mensualité qui n'est pas échue. Pour un échéancier personnalisé, seule
        /// la première échéance (SequenceNo 1) est marquée.
        /// </summary>
        bool IsInitialScope = false);

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
                Allocate($"PLAN-{installment.Id}", installment.Label, installment.Amount, installment.DueDate,
                    feeCategoryId: null, isInitialScope: installment.SequenceNo == 1);
            }

            return result;
        }

        foreach (var line in feeLines)
        {
            if (!line.IsRecurring || line.Months <= 1)
            {
                Allocate($"LINE-{line.Id}", line.Designation, line.LineTotal, schoolYearStart,
                    feeCategoryId: line.FeeCategoryId, isInitialScope: true);
            }
            else
            {
                for (var m = 1; m <= line.Months; m++)
                {
                    Allocate(
                        $"MONTH-{line.Id}-{m}",
                        $"{line.Designation} (Mois {m})",
                        line.UnitAmount,
                        schoolYearStart.AddMonths(m - 1),
                        feeCategoryId: line.FeeCategoryId,
                        // Seul le premier mois relève de l'engagement initial (un mois d'avance).
                        isInitialScope: m == 1);
                }
            }
        }

        return result;

        void Allocate(string id, string label, decimal amount, DateOnly dueDate, Guid? feeCategoryId, bool isInitialScope)
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

            result.Add(new CalculatedInstallment(
                id, label, amount, paidForThis, remainingDue, dueDate, status, feeCategoryId, isInitialScope));
        }
    }
}
