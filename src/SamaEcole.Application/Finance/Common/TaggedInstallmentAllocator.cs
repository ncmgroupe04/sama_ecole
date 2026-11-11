using SamaEcole.Application.Finance.Common;

namespace SamaEcole.Application.Finance.Common;

/// <summary>
/// Répartit l'argent versé entre les échéances en tenant compte du MOIS CHOISI par le caissier (option « demander le
/// mois à l'encaissement », <c>PaymentBreakdown.Month</c>) :
///   1. l'argent d'une ligne DÉSIGNANT un mois va sur ce mois (dans la limite de son montant, l'excédent retombe dans
///      le reste) ;
///   2. l'argent SANS mois désigné (tous les anciens paiements, les caissiers qui ne choisissent pas) remplit ce qui
///      reste, dans l'ordre — frais ponctuels d'abord, puis les mois — exactement comme InstallmentScheduleCalculator,
///      avec en option la pratique d'école « le premier mois versé à l'inscription est le dernier mois ».
///
/// SANS mois désignés et SANS la pratique, le résultat est IDENTIQUE à InstallmentScheduleCalculator : aucun chiffre
/// existant ne change tant qu'on n'utilise pas ces deux possibilités. Les frais ponctuels (« LINE- ») ne reçoivent jamais
/// d'argent désigné par mois. Un échéancier personnalisé (« PLAN- ») se lit par le mois de sa date d'échéance.
/// </summary>
public static class TaggedInstallmentAllocator
{
    public record Allocated(
        InstallmentScheduleCalculator.CalculatedInstallment Installment,
        decimal TaggedPaid,
        decimal UntaggedPaid,
        decimal UntaggedCumulative);

    /// <param name="schedule">Échéances calculées (seuls montants, ids, dates et catégories servent).</param>
    /// <param name="totalPaid">Cumul encaissé net (Enrollment.AmountPaid, annulations déjà déduites).</param>
    /// <param name="taggedByMonth">Argent désignant un mois civil (1-12) — paiements non annulés uniquement.</param>
    public static IReadOnlyList<Allocated> Allocate(
        IReadOnlyList<InstallmentScheduleCalculator.CalculatedInstallment> schedule,
        decimal totalPaid,
        IReadOnlyDictionary<int, decimal>? taggedByMonth,
        DateOnly today,
        bool firstMonthCoversLast)
    {
        var ordered = firstMonthCoversLast ? FirstMonthLast(schedule) : schedule;
        var taggedLeft = taggedByMonth is null
            ? new Dictionary<int, decimal>()
            : new Dictionary<int, decimal>(taggedByMonth);

        // Passe 1 : l'argent désigné va sur son mois.
        var tagged = new decimal[ordered.Count];
        var taggedUsed = 0m;
        for (var i = 0; i < ordered.Count; i++)
        {
            var inst = ordered[i];
            if (inst.Id.StartsWith("LINE-", StringComparison.Ordinal)
                || !taggedLeft.TryGetValue(inst.DueDate.Month, out var left) || left <= 0)
            {
                continue;
            }

            var take = Math.Min(left, inst.Amount);
            tagged[i] = take;
            taggedLeft[inst.DueDate.Month] = left - take;
            taggedUsed += take;
        }

        // Passe 2 : le reste (non désigné, ou excédent désigné) remplit dans l'ordre.
        var pool = Math.Max(0m, totalPaid - taggedUsed);
        var result = new List<Allocated>(ordered.Count);
        var untaggedCumulative = 0m;
        for (var i = 0; i < ordered.Count; i++)
        {
            var inst = ordered[i];
            var fromPool = Math.Min(pool, inst.Amount - tagged[i]);
            pool -= fromPool;
            untaggedCumulative += fromPool;

            var paid = tagged[i] + fromPool;
            var remaining = inst.Amount - paid;
            var status = remaining <= 0
                ? InstallmentScheduleCalculator.InstallmentState.Paid
                : paid > 0
                    ? InstallmentScheduleCalculator.InstallmentState.Partial
                    : inst.DueDate < today
                        ? InstallmentScheduleCalculator.InstallmentState.Overdue
                        : InstallmentScheduleCalculator.InstallmentState.Pending;

            result.Add(new Allocated(
                inst with { AmountPaid = paid, RemainingDue = remaining, Status = status },
                tagged[i], fromPool, untaggedCumulative));
        }

        return result;
    }

    /// <summary>
    /// Pratique d'école : pour chaque frais récurrent dérivé (« MONTH-{ligne}-{m} »), le DERNIER mois est servi en
    /// premier ; les mois 1..N-1 suivent dans l'ordre. Les frais ponctuels et les plans personnalisés restent en tête, inchangés.
    /// </summary>
    private static IReadOnlyList<InstallmentScheduleCalculator.CalculatedInstallment> FirstMonthLast(
        IReadOnlyList<InstallmentScheduleCalculator.CalculatedInstallment> schedule)
    {
        var ordered = schedule.Where(i => !i.Id.StartsWith("MONTH-", StringComparison.Ordinal)).ToList();

        foreach (var line in schedule
                     .Where(i => i.Id.StartsWith("MONTH-", StringComparison.Ordinal))
                     .GroupBy(i => i.Id[..i.Id.LastIndexOf('-')]))
        {
            var months = line.ToList();
            ordered.Add(months[^1]);
            ordered.AddRange(months.Take(months.Count - 1));
        }

        return ordered;
    }
}
