using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;

/// <summary>
/// Calcule les débiteurs de l'école courante et prépare un lot BROUILLON par classe (Étape 5 —
/// recouvrement semi-automatique). N'envoie AUCUN SMS elle-même : §13.7 du cahier des charges interdit
/// l'envoi de masse automatique, seul SendDebtorReminderBatchCommand (déclenché à la main par un
/// Directeur/Finance) fait effectivement partir les messages d'un lot.
///
/// Déclenchée une fois par nuit et par école par DebtorAgingHostedService, sous
/// TenantProvider.RunAsSchoolAsync — le "tenant courant" ici est celui posé par l'override, jamais un
/// JWT. Peut aussi être appelée manuellement (ex. bouton "Recalculer" côté écran Recouvrement).
/// </summary>
public record GenerateDebtorReminderBatchesCommand : IRequest<int>;

public class GenerateDebtorReminderBatchesCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider,
    ILogger<GenerateDebtorReminderBatchesCommandHandler> logger)
    : IRequestHandler<GenerateDebtorReminderBatchesCommand, int>
{
    public async Task<int> Handle(GenerateDebtorReminderBatchesCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Double interrupteur, comme partout ailleurs sur les relances (SendDuesReminderSmsCommand) :
        // aucun brouillon n'est généré tant que le Directeur n'a pas explicitement activé les relances
        // SMS — générer des lots qu'aucune école n'a demandés serait un travail inutile et confus.
        var settings = await dbContext.SchoolSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            // Distinct d'un opt-out délibéré (SmsOnDuesReminder = false, cas normal ci-dessous) :
            // une école sans aucune ligne SchoolSettings n'a jamais été paramétrée — un signal utile
            // pour repérer un établissement mal provisionné, invisible tant que ce tour ne fait que
            // rendre 0 silencieusement (DebtorAgingHostedService.cs).
            logger.LogWarning(
                "Calcul des lots de relance ignoré pour l'établissement {SchoolId} : aucun paramétrage (SchoolSettings) configuré.",
                schoolId);
            return 0;
        }

        if (!settings.SmsOnDuesReminder)
        {
            return 0;
        }

        var hasActiveSchoolYear = await dbContext.SchoolYears.AsNoTracking()
            .AnyAsync(y => y.IsActive, cancellationToken);
        if (!hasActiveSchoolYear)
        {
            logger.LogWarning(
                "Calcul des lots de relance ignoré pour l'établissement {SchoolId} : aucune année scolaire active.",
                schoolId);
            return 0;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        // Ne jamais empiler : une classe qui a déjà un brouillon EN ATTENTE n'en reçoit pas un second —
        // c'est à l'humain de l'envoyer ou de l'écarter avant qu'un nouveau calcul ne la reprenne.
        var classroomsWithDraft = await dbContext.DebtorReminderBatches.AsNoTracking()
            .Where(b => b.Status == DebtorReminderBatchStatus.Draft)
            .Select(b => b.ClassroomId)
            .ToListAsync(cancellationToken);

        var debtors = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.Status == EnrollmentStatus.Confirmed
                && e.AmountPaid < e.TotalDue
                && y.IsActive
                && !classroomsWithDraft.Contains(e.ClassroomId)
            select new { Enrollment = e, Student = s, SchoolYearStart = y.StartDate })
            .ToListAsync(cancellationToken);

        if (debtors.Count == 0)
        {
            return 0;
        }

        // Chargées une seule fois pour tous les débiteurs (au lieu de 2 requêtes par débiteur) —
        // corrige une régression N+1 introduite lors de la fiabilisation du calcul (commit e7005bf).
        var debtorEnrollmentIds = debtors.Select(d => d.Enrollment.Id).ToList();

        var feeLinesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => debtorEnrollmentIds.Contains(l.EnrollmentId))
                .OrderBy(l => l.IsRecurring)
                .ThenBy(l => l.Designation)
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var installmentsByEnrollment = (await dbContext.FeeInstallmentPlans.AsNoTracking()
                .Where(p => debtorEnrollmentIds.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active)
                .SelectMany(p => dbContext.FeeInstallments.Where(i => i.FeeInstallmentPlanId == p.Id), (p, i) => new { p.EnrollmentId, Installment = i })
                .OrderBy(x => x.Installment.SequenceNo)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Installment).ToList());

        var createdCount = 0;

        foreach (var group in debtors.GroupBy(d => d.Enrollment.ClassroomId))
        {
            var items = new List<DebtorReminderBatchItem>();

            foreach (var debtor in group)
            {
                var lines = feeLinesByEnrollment.GetValueOrDefault(debtor.Enrollment.Id, []);
                var customInstallments = installmentsByEnrollment.GetValueOrDefault(debtor.Enrollment.Id, []);

                var overdue = InstallmentScheduleCalculator
                    .Calculate(debtor.Enrollment.AmountPaid, today, lines, debtor.SchoolYearStart, customInstallments)
                    .Where(c => c.RemainingDue > 0 && c.DueDate < today)
                    .ToList();

                if (overdue.Count == 0)
                {
                    // Un solde dû dont aucune échéance n'est encore dépassée (tout est Pending/Partial
                    // à venir) n'est pas un retard : rien à relancer aujourd'hui.
                    continue;
                }

                // Ancienneté = plus ancienne échéance encore impayée, pas la moyenne : c'est ce qui
                // justifie une relance, même si des échéances plus récentes ont déjà été soldées.
                var oldestDueDate = overdue.Min(c => c.DueDate);
                var daysOverdue = today.DayNumber - oldestDueDate.DayNumber;

                if (daysOverdue < settings.DebtorReminderThresholdDays)
                {
                    continue;
                }

                items.Add(new DebtorReminderBatchItem
                {
                    SchoolId = schoolId,
                    EnrollmentId = debtor.Enrollment.Id,
                    StudentId = debtor.Student.Id,
                    GuardianPhone = debtor.Student.GuardianPhone,
                    RemainingBalance = debtor.Enrollment.TotalDue - debtor.Enrollment.AmountPaid,
                    DaysOverdue = daysOverdue
                });
            }

            if (items.Count == 0)
            {
                continue;
            }

            var batch = new DebtorReminderBatch
            {
                SchoolId = schoolId,
                ClassroomId = group.Key,
                ThresholdDays = settings.DebtorReminderThresholdDays,
                GeneratedAt = timeProvider.GetUtcNow(),
                Status = DebtorReminderBatchStatus.Draft
            };
            dbContext.DebtorReminderBatches.Add(batch);

            foreach (var item in items)
            {
                item.DebtorReminderBatchId = batch.Id;
                dbContext.DebtorReminderBatchItems.Add(item);
            }

            createdCount++;
        }

        if (createdCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return createdCount;
    }
}
