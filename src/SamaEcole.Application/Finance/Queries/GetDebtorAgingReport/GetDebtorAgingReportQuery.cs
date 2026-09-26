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

        // Lignes de frais et échéanciers actifs chargés EN LOT pour tous les débiteurs (deux requêtes au
        // total) plutôt que deux requêtes PAR débiteur : ToLookup conserve l'ordre SQL au sein de chaque
        // inscription (IsRecurring puis Designation ; SequenceNo), celui qu'attend le calculateur.
        var enrollmentIds = debtors.Select(d => d.Enrollment.Id).ToList();

        var linesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => enrollmentIds.Contains(l.EnrollmentId))
                .OrderBy(l => l.IsRecurring)
                .ThenBy(l => l.Designation)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.EnrollmentId);

        var installmentsByEnrollment = (await (
                    from p in dbContext.FeeInstallmentPlans.AsNoTracking()
                    where enrollmentIds.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active
                    join i in dbContext.FeeInstallments.AsNoTracking() on p.Id equals i.FeeInstallmentPlanId
                    orderby i.SequenceNo
                    select new { p.EnrollmentId, Installment = i })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.EnrollmentId, x => x.Installment);

        var rows = new List<DebtorAgingRow>();

        foreach (var debtor in debtors)
        {
            var lines = linesByEnrollment[debtor.Enrollment.Id].ToList();
            var customInstallments = installmentsByEnrollment[debtor.Enrollment.Id].ToList();

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
