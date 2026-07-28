using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFinanceDashboard;

/// <summary>
/// GET /finance/dashboard — ticket JGK-F04. Vue d'ensemble financière de l'école : ce qui a été
/// encaissé (jour/mois/année), ce qui reste dû, et la part déjà recouvrée sur l'année scolaire ACTIVE.
///
/// Lecture réservée à Directeur et Finance (pas Secrétariat) : composer le montant dû à l'inscription
/// n'est pas la même chose que voir la santé financière agrégée de l'établissement — cohérent avec la
/// séparation des rôles de la règle #4 (AGENTS.md).
/// </summary>
public record GetFinanceDashboardQuery : IRequest<FinanceDashboardDto>;

public record FinanceDashboardDto(
    decimal CollectedToday,
    decimal CollectedThisMonth,
    decimal CollectedThisYear,
    decimal OutstandingBalance,
    decimal RecoveryRate,
    IReadOnlyList<RecentPaymentDto> RecentPayments,
    decimal ExpectedThisMonth = 0m,
    decimal MonthlyRecoveryRate = 0m,
    decimal ExpectedThisYear = 0m,
    decimal YearlyRecoveryRate = 0m,
    decimal TotalDisbursements = 0m,
    decimal RealBalance = 0m,
    IReadOnlyList<DisbursementCategoryTotal>? DisbursementsByCategory = null);

public record DisbursementCategoryTotal(string Category, decimal Amount);

public record RecentPaymentDto(
    Guid PaymentId,
    string ReceiptNumber,
    string Matricule,
    string StudentFullName,
    decimal Amount,
    string Method,
    DateTimeOffset PaidAt);

public class GetFinanceDashboardQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetFinanceDashboardQuery, FinanceDashboardDto>
{
    /// <summary>Combien de derniers versements affichés sous les cartes KPI (docs/design-references/dashboard-reference.jpg).</summary>
    private const int RecentPaymentsCount = 10;

    public async Task<FinanceDashboardDto> Handle(GetFinanceDashboardQuery request, CancellationToken cancellationToken)
    {
        // "Aujourd'hui" vient de TimeProvider, jamais de DateTime.UtcNow en dur : les tests contrôlent
        // l'horloge exactement comme CreateEnrollmentCommandHandler.
        var now = timeProvider.GetUtcNow();
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset);

        // Un versement annulé (règle #6 : jamais de suppression physique, un Payment se corrige par
        // Status = Cancelled) ne doit compter dans AUCUN total encaissé.
        var validPayments = dbContext.Payments.AsNoTracking()
            .Where(p => p.Status != PaymentStatus.Cancelled);

        var collectedToday = await validPayments
            .Where(p => p.PaidAt >= todayStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var collectedThisMonth = await validPayments
            .Where(p => p.PaidAt >= monthStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var collectedThisYear = await validPayments
            .Where(p => p.PaidAt >= yearStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        // Solde dû et taux de recouvrement portent sur les inscriptions NON ANNULÉES de l'année scolaire
        // ACTIVE — le même périmètre que la caisse (GetStudentBalanceQuery), pas l'historique complet.
        var activeYear = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        var activeEnrollments = dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Status != EnrollmentStatus.Cancelled && activeYear != null && e.SchoolYearId == activeYear.Id);

        var totals = await activeEnrollments
            .Select(e => new { e.TotalDue, e.AmountPaid })
            .ToListAsync(cancellationToken);

        var totalDue = totals.Sum(t => t.TotalDue);
        var totalPaid = totals.Sum(t => t.AmountPaid);
        var outstandingBalance = totalDue - totalPaid;
        var recoveryRate = totalDue > 0 ? totalPaid / totalDue : 0m;

        decimal expectedThisMonth = 0m;
        if (activeYear != null)
        {
            int monthIndex = (now.Year - activeYear.StartDate.Year) * 12 + now.Month - activeYear.StartDate.Month + 1;
            if (monthIndex >= 1)
            {
                var feeLines = await dbContext.EnrollmentFeeLines.AsNoTracking()
                    .Where(l => dbContext.Enrollments.Any(e => e.Id == l.EnrollmentId && e.Status != EnrollmentStatus.Cancelled && e.SchoolYearId == activeYear.Id))
                    .ToListAsync(cancellationToken);

                foreach (var line in feeLines)
                {
                    if (monthIndex == 1 && (!line.IsRecurring || line.Months <= 1))
                    {
                        expectedThisMonth += line.LineTotal;
                    }
                    if (line.IsRecurring && line.Months >= monthIndex)
                    {
                        expectedThisMonth += line.UnitAmount;
                    }
                }
            }
        }

        var monthlyRecoveryRate = expectedThisMonth > 0 ? Math.Min(1.0m, collectedThisMonth / expectedThisMonth) : (collectedThisMonth > 0 ? 1.0m : 0m);

        var recentPayments = await (
            from p in dbContext.Payments.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on p.EnrollmentId equals e.Id
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            where p.Status != PaymentStatus.Cancelled
            orderby p.PaidAt descending
            select new RecentPaymentDto(
                p.Id, p.ReceiptNumber, s.Matricule, s.FullName, p.Amount, p.Method.ToString(), p.PaidAt))
            .Take(RecentPaymentsCount)
            .ToListAsync(cancellationToken);

        var totalDisbursements = await dbContext.Disbursements.AsNoTracking()
            .SumAsync(d => d.Amount, cancellationToken);
            
        var realBalance = collectedThisYear - totalDisbursements;

        var disbursementsByCategory = await dbContext.Disbursements.AsNoTracking()
            .GroupBy(d => d.Category)
            .Select(g => new DisbursementCategoryTotal(g.Key.ToString(), g.Sum(d => d.Amount)))
            .ToListAsync(cancellationToken);

        return new FinanceDashboardDto(
            collectedToday, collectedThisMonth, collectedThisYear,
            outstandingBalance, recoveryRate, recentPayments,
            expectedThisMonth, monthlyRecoveryRate, totalDue, recoveryRate,
            totalDisbursements, realBalance, disbursementsByCategory);
    }
}
