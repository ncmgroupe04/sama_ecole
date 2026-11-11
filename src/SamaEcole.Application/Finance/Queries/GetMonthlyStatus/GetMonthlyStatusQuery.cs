using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

/// <summary>
/// GET /finance/monthly-status — Situation des mensualités : pour une année scolaire, un mois civil (1-12) et
/// éventuellement une classe, qui a payé, payé partiellement ou rien payé.
///
/// AUCUN second système de paiement. Deux sources pour savoir à quel mois va l'argent :
///   1. le MOIS CHOISI par le caissier à l'encaissement (<c>PaymentBreakdown.Month</c>), quand l'école l'utilise ;
///   2. à défaut, l'imputation déjà utilisée partout (solde élève, sommation, débiteurs) : le cumul
///      <c>Enrollment.AmountPaid</c> est alloué échéance par échéance, avec en option la pratique d'école
///      « le premier mois versé à l'inscription est le dernier mois ».
/// Voir <see cref="TaggedInstallmentAllocator"/>. Les paiements annulés ou contre-passés sont déjà déduits d'AmountPaid.
/// </summary>
public record GetMonthlyStatusQuery(Guid SchoolYearId, int Month, Guid? ClassroomId = null)
    : IRequest<MonthlyStatusDto>;

public sealed class GetMonthlyStatusQueryValidator : AbstractValidator<GetMonthlyStatusQuery>
{
    public GetMonthlyStatusQueryValidator()
    {
        RuleFor(x => x.SchoolYearId).NotEmpty();
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}

public record MonthlyStatusRow(
    Guid EnrollmentId,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    decimal ExpectedAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    DateTimeOffset? LastPaymentAt);

public record MonthlyStatusSummary(
    int TotalStudents,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    decimal TotalExpected,
    decimal TotalCollected,
    decimal TotalRemaining);

public record MonthlyStatusDto(
    string SchoolYearLabel,
    int Month,
    Guid? ClassroomId,
    MonthlyStatusSummary Summary,
    IReadOnlyList<MonthlyStatusRow> Paid,
    IReadOnlyList<MonthlyStatusRow> Partial,
    IReadOnlyList<MonthlyStatusRow> Unpaid);

/// <summary>Règle pure de calcul d'un mois pour UNE inscription — isolée pour être testée sans base.</summary>
public static class MonthlyStatusCalculator
{
    public enum Outcome { NotConcerned, Paid, Partial, Unpaid }

    public record Result(Outcome Outcome, decimal Expected, decimal Paid, DateTimeOffset? LastPaymentAt);

    /// <param name="payments">Part SANS mois choisi de chaque versement non annulé (montant, date).</param>
    /// <param name="taggedPayments">Lignes de versement non annulé qui DÉSIGNENT un mois (mois civil, montant, date).</param>
    public static Result Compute(
        IReadOnlyList<InstallmentScheduleCalculator.CalculatedInstallment> schedule,
        int month,
        EnrollmentStatus status,
        DateTimeOffset enrolledAt,
        int yearOfMonth,
        IReadOnlyList<(decimal Amount, DateTimeOffset PaidAt)> payments,
        bool firstMonthCoversLast = false,
        IReadOnlyList<(int Month, decimal Amount, DateTimeOffset PaidAt)>? taggedPayments = null)
    {
        taggedPayments ??= [];
        var totalPaid = schedule.Sum(i => i.AmountPaid);
        var taggedByMonth = taggedPayments
            .GroupBy(t => t.Month)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var allocated = TaggedInstallmentAllocator.Allocate(
            schedule, totalPaid, taggedByMonth, today: DateOnly.MinValue, firstMonthCoversLast);

        // Seules les mensualités (récurrentes dérivées, ou échéances d'un plan personnalisé) sont des « mois » ;
        // les frais ponctuels (Id « LINE- ») ne le sont pas.
        var expected = 0m;
        var paid = 0m;
        var taggedPaid = 0m;
        var untaggedThreshold = 0m;
        var untaggedPaid = 0m;

        foreach (var a in allocated)
        {
            if (a.Installment.Id.StartsWith("LINE-", StringComparison.Ordinal) || a.Installment.DueDate.Month != month)
            {
                continue;
            }

            expected += a.Installment.Amount;
            paid += a.Installment.AmountPaid;
            taggedPaid += a.TaggedPaid;
            untaggedPaid += a.UntaggedPaid;
            untaggedThreshold = a.UntaggedCumulative;
        }

        if (expected <= 0)
        {
            return new Result(Outcome.NotConcerned, 0m, 0m, null);
        }

        // Pas concerné s'il n'était pas là : inscrit après la fin du mois, ou parti (abandon/transfert) — sauf s'il
        // a déjà versé pour ce mois (on n'efface jamais un encaissement de la vue).
        var monthEnd = new DateTimeOffset(yearOfMonth, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        var leftOrNotYetThere = enrolledAt >= monthEnd
            || status is EnrollmentStatus.DroppedOut or EnrollmentStatus.Transferred;
        if (leftOrNotYetThere && paid <= 0)
        {
            return new Result(Outcome.NotConcerned, expected, 0m, null);
        }

        if (paid >= expected)
        {
            // Dernier versement utile : le plus tardif entre celui qui désignait ce mois et celui qui a soldé la part non désignée.
            DateTimeOffset? last = null;
            if (taggedPaid > 0)
            {
                last = taggedPayments.Where(t => t.Month == month).Max(t => (DateTimeOffset?)t.PaidAt);
            }

            if (untaggedPaid > 0)
            {
                var date = SettlingPaymentDate(payments, untaggedThreshold);
                if (date is { } d && (last is null || d > last)) last = d;
            }

            return new Result(Outcome.Paid, expected, expected, last);
        }

        return paid > 0
            ? new Result(Outcome.Partial, expected, paid, null)
            : new Result(Outcome.Unpaid, expected, 0m, null);
    }

    /// <summary>Date du versement (part non désignée) qui a fait franchir le seuil cumulé de la mensualité.</summary>
    private static DateTimeOffset? SettlingPaymentDate(
        IReadOnlyList<(decimal Amount, DateTimeOffset PaidAt)> payments, decimal threshold)
    {
        if (payments.Count == 0)
        {
            return null;
        }

        var running = 0m;
        foreach (var payment in payments.OrderBy(p => p.PaidAt))
        {
            running += payment.Amount;
            if (running >= threshold)
            {
                return payment.PaidAt;
            }
        }

        return payments.Max(p => p.PaidAt);
    }
}

public class GetMonthlyStatusQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetMonthlyStatusQuery, MonthlyStatusDto>
{
    public async Task<MonthlyStatusDto> Handle(GetMonthlyStatusQuery request, CancellationToken cancellationToken)
    {
        var year = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {request.SchoolYearId} introuvable.");

        // Le mois civil appartient à l'une des deux années civiles de l'année scolaire : on retient celle où il tombe.
        var yearOfMonth = request.Month >= year.StartDate.Month ? year.StartDate.Year : year.StartDate.Year + 1;

        // Pratique de l'école (réglage du Directeur) : le mois versé avec l'inscription est le dernier mois.
        var firstCoversLast = await dbContext.SchoolSettings.AsNoTracking()
            .Select(s => (bool?)s.FirstMonthlyPaymentCoversLastMonth)
            .FirstOrDefaultAsync(cancellationToken) ?? false;

        var rows = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            where e.SchoolYearId == year.Id
                && e.Status != EnrollmentStatus.Cancelled
                && (request.ClassroomId == null || e.ClassroomId == request.ClassroomId)
            select new
            {
                e.Id,
                e.Status,
                e.EnrolledAt,
                e.AmountPaid,
                s.Matricule,
                s.FullName,
                ClassroomName = c.Name
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();

        var linesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => ids.Contains(l.EnrollmentId))
                .OrderBy(l => l.IsRecurring)
                .ThenBy(l => l.Designation)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.EnrollmentId);

        var installmentsByEnrollment = (await (
                    from p in dbContext.FeeInstallmentPlans.AsNoTracking()
                    where ids.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active
                    join i in dbContext.FeeInstallments.AsNoTracking() on p.Id equals i.FeeInstallmentPlanId
                    orderby i.SequenceNo
                    select new { p.EnrollmentId, Installment = i })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.EnrollmentId, x => x.Installment);

        // Versements non annulés, avec leurs lignes qui désignent un mois (le cas échéant).
        var paymentRows = await dbContext.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.EnrollmentId) && p.Status != PaymentStatus.Cancelled)
            .Select(p => new
            {
                p.EnrollmentId,
                p.Amount,
                p.PaidAt,
                Tagged = p.Breakdowns
                    .Where(b => b.Month != null)
                    .Select(b => new { Month = b.Month!.Value, b.AmountAllocated })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var untaggedByEnrollment = paymentRows
            .ToLookup(p => p.EnrollmentId, p => (Amount: p.Amount - p.Tagged.Sum(t => t.AmountAllocated), p.PaidAt));
        var taggedByEnrollment = paymentRows
            .SelectMany(p => p.Tagged.Select(t => (p.EnrollmentId, t.Month, Amount: t.AmountAllocated, p.PaidAt)))
            .ToLookup(t => t.EnrollmentId, t => (t.Month, t.Amount, t.PaidAt));

        var paid = new List<MonthlyStatusRow>();
        var partial = new List<MonthlyStatusRow>();
        var unpaid = new List<MonthlyStatusRow>();

        foreach (var r in rows)
        {
            var schedule = InstallmentScheduleCalculator.Calculate(
                r.AmountPaid,
                DateOnly.FromDateTime(DateTime.UtcNow),
                linesByEnrollment[r.Id].ToList(),
                year.StartDate,
                installmentsByEnrollment[r.Id].ToList());

            var result = MonthlyStatusCalculator.Compute(
                schedule, request.Month, r.Status, r.EnrolledAt, yearOfMonth,
                untaggedByEnrollment[r.Id].Where(p => p.Amount > 0).ToList(),
                firstCoversLast,
                taggedByEnrollment[r.Id].ToList());

            if (result.Outcome == MonthlyStatusCalculator.Outcome.NotConcerned)
            {
                continue;
            }

            var row = new MonthlyStatusRow(
                r.Id, r.Matricule, r.FullName, r.ClassroomName,
                result.Expected, result.Paid, result.Expected - result.Paid, result.LastPaymentAt);

            (result.Outcome switch
            {
                MonthlyStatusCalculator.Outcome.Paid => paid,
                MonthlyStatusCalculator.Outcome.Partial => partial,
                _ => unpaid
            }).Add(row);
        }

        static List<MonthlyStatusRow> Sort(List<MonthlyStatusRow> list) => list
            .OrderBy(x => x.ClassroomName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.StudentFullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        paid = Sort(paid);
        partial = Sort(partial);
        unpaid = Sort(unpaid);

        var all = paid.Concat(partial).Concat(unpaid).ToList();
        var summary = new MonthlyStatusSummary(
            all.Count, paid.Count, partial.Count, unpaid.Count,
            all.Sum(x => x.ExpectedAmount),
            all.Sum(x => x.PaidAmount),
            all.Sum(x => x.RemainingAmount));

        return new MonthlyStatusDto(year.Label, request.Month, request.ClassroomId, summary, paid, partial, unpaid);
    }
}
