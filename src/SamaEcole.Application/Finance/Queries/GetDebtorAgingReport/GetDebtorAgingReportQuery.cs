using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;

/// <summary>
/// Rapport comptable des débiteurs de l'établissement (Étape 5), en LECTURE SEULE et TOUJOURS actuel
/// (pas limité aux classes déjà couvertes par un lot de relance brouillon, contrairement à
/// GenerateDebtorReminderBatchesCommand) — répond au Volume 1 §7.5. Réutilisé tel quel par l'export
/// Excel (GetDebtorAgingExportQuery), pour que l'écran et le fichier téléchargé portent exactement
/// les mêmes chiffres (même principe que GetRevenueConsolidation/GetRevenueConsolidationExcel).
/// </summary>
public record GetDebtorAgingReportQuery : IRequest<DebtorAgingReportDto>;

public record DebtorAgingReportDto(DateOnly GeneratedOn, IReadOnlyList<DebtorAgingRow> Debtors);

public record DebtorAgingRow(
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    decimal RemainingBalance,
    int DaysOverdue,
    string? GuardianPhone);

public class GetDebtorAgingReportQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetDebtorAgingReportQuery, DebtorAgingReportDto>
{
    public async Task<DebtorAgingReportDto> Handle(
        GetDebtorAgingReportQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        var debtors = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.Status == EnrollmentStatus.Confirmed && e.AmountPaid < e.TotalDue && y.IsActive
            select new
            {
                Enrollment = e,
                Student = s,
                ClassroomName = c.Name,
                SchoolYearStart = y.StartDate
            })
            .ToListAsync(cancellationToken);

        var rows = new List<DebtorAgingRow>();

        foreach (var debtor in debtors)
        {
            var lines = await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => l.EnrollmentId == debtor.Enrollment.Id)
                .OrderBy(l => l.IsRecurring)
                .ThenBy(l => l.Designation)
                .ToListAsync(cancellationToken);

            var customInstallments = await dbContext.FeeInstallmentPlans.AsNoTracking()
                .Where(p => p.EnrollmentId == debtor.Enrollment.Id && p.Status == FeeInstallmentPlanStatus.Active)
                .SelectMany(p => dbContext.FeeInstallments.Where(i => i.FeeInstallmentPlanId == p.Id))
                .OrderBy(i => i.SequenceNo)
                .ToListAsync(cancellationToken);

            var overdue = InstallmentScheduleCalculator
                .Calculate(debtor.Enrollment.AmountPaid, today, lines, debtor.SchoolYearStart, customInstallments)
                .Where(c => c.RemainingDue > 0 && c.DueDate < today)
                .ToList();

            if (overdue.Count == 0)
            {
                // Solde dû mais aucune échéance encore dépassée : pas un retard, hors du rapport.
                continue;
            }

            var oldestDueDate = overdue.Min(c => c.DueDate);
            var daysOverdue = today.DayNumber - oldestDueDate.DayNumber;

            rows.Add(new DebtorAgingRow(
                debtor.Student.Matricule,
                debtor.Student.FullName,
                debtor.ClassroomName,
                debtor.Enrollment.TotalDue - debtor.Enrollment.AmountPaid,
                daysOverdue,
                debtor.Student.GuardianPhone));
        }

        return new DebtorAgingReportDto(today, rows.OrderByDescending(r => r.DaysOverdue).ToList());
    }
}
