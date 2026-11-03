using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Notifications.Commands.SendDuesReminderSms;

/// <summary>
/// POST /sms/dues-reminders — relance par SMS les tuteurs des élèves qui ont au moins une échéance
/// EN RETARD (date d'échéance dépassée et solde impayé, même calcul que le rapport des débiteurs et
/// les lots de relance), pour le montant en retard. Un élève dont le solde ne tombe que sur des
/// échéances à venir — année non commencée, mensualités à jour — n'est PAS relancé : lui écrire
/// « reliquat de 120 000 FCFA » serait une erreur visible par une famille à jour.
/// Déclenchée à la main par le Directeur ou la Finance (aucun envoi de masse automatique : écrire à
/// toutes les familles d'un coup est une décision, pas un effet de bord).
///
/// Portée facultative par classe : relancer une classe entière est le geste courant en fin de mois ;
/// sans <see cref="ClassroomId"/>, la relance couvre tout l'établissement.
///
/// N'ÉCHOUE PAS si un SMS ne part pas : le résultat rapporte combien ont été MIS EN FILE et combien
/// ont été écartés (sans numéro, solde épuisé…), ce qui laisse l'utilisateur décider de la suite.
/// </summary>
public record SendDuesReminderSmsCommand : IRequest<DuesReminderSmsResult>
{
    public Guid? ClassroomId { get; init; }
}

/// <summary>
/// <paramref name="QueuedCount"/> compte les messages ACCEPTÉS EN FILE, pas les messages remis : la
/// remise est asynchrone (SmsQueueProcessor) et n'est pas connue au retour de la requête. Nommer ce
/// compteur « Sent » afficherait à la Finance un nombre d'envois réussis que personne n'a encore
/// constaté. L'issue réelle se consulte dans l'historique.
/// </summary>
public record DuesReminderSmsResult(int QueuedCount, int SkippedCount, string? FirstSkipReason);

public class SendDuesReminderSmsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISmsDispatcher smsDispatcher,
    TimeProvider timeProvider,
    ILogger<SendDuesReminderSmsCommandHandler> logger)
    : IRequestHandler<SendDuesReminderSmsCommand, DuesReminderSmsResult>
{
    public async Task<DuesReminderSmsResult> Handle(
        SendDuesReminderSmsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter borne déjà les deux tables au tenant courant. Seules les
        // inscriptions CONFIRMÉES sont relancées : une inscription annulée ou transférée ne doit plus
        // rien, la relancer serait une erreur visible par la famille.
        var candidates = await (
            from enrollment in dbContext.Enrollments.AsNoTracking()
            where enrollment.Status == EnrollmentStatus.Confirmed
                  && enrollment.AmountPaid < enrollment.TotalDue
                  && (request.ClassroomId == null || enrollment.ClassroomId == request.ClassroomId)
            join student in dbContext.Students.AsNoTracking() on enrollment.StudentId equals student.Id
            join year in dbContext.SchoolYears.AsNoTracking() on enrollment.SchoolYearId equals year.Id
            select new
            {
                EnrollmentId = enrollment.Id,
                enrollment.AmountPaid,
                SchoolYearStart = year.StartDate,
                student.Id,
                student.FullName,
                student.GuardianPhone
            })
            .ToListAsync(cancellationToken);

        // Échéances chargées une seule fois pour tous les candidats (pas de N+1), dans l'ordre que
        // le calculateur attend : lignes (IsRecurring puis Designation), plan personnalisé par SequenceNo.
        var enrollmentIds = candidates.Select(c => c.EnrollmentId).ToList();

        var feeLinesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => enrollmentIds.Contains(l.EnrollmentId))
                .OrderBy(l => l.IsRecurring)
                .ThenBy(l => l.Designation)
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EnrollmentFeeLine>)g.ToList());

        var customInstallmentsByEnrollment = (await dbContext.FeeInstallmentPlans.AsNoTracking()
                .Where(p => enrollmentIds.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active)
                .SelectMany(p => dbContext.FeeInstallments.Where(i => i.FeeInstallmentPlanId == p.Id),
                    (p, i) => new { p.EnrollmentId, Installment = i })
                .OrderBy(x => x.Installment.SequenceNo)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FeeInstallment>)g.Select(x => x.Installment).ToList());

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        var debtors = candidates
            .Select(c => new
            {
                c.Id,
                c.FullName,
                c.GuardianPhone,
                Overdue = InstallmentScheduleCalculator
                    .Calculate(
                        c.AmountPaid,
                        today,
                        feeLinesByEnrollment.GetValueOrDefault(c.EnrollmentId) ?? [],
                        c.SchoolYearStart,
                        customInstallmentsByEnrollment.GetValueOrDefault(c.EnrollmentId))
                    .Where(i => i.RemainingDue > 0 && i.DueDate < today)
                    .Sum(i => i.RemainingDue)
            })
            .Where(d => d.Overdue > 0)
            .ToList();

        var queuedCount = 0;
        var skippedCount = 0;
        string? firstSkipReason = null;

        foreach (var debtor in debtors)
        {
            var body =
                $"Rappel : la scolarité de {debtor.FullName} présente un retard de "
                + $"{debtor.Overdue:N0} FCFA. Merci de régulariser auprès de l'établissement.";

            var outcome = await smsDispatcher.DispatchAsync(
                new SmsDispatchRequest(schoolId, debtor.GuardianPhone, body, SmsTrigger.DuesReminder, debtor.Id),
                cancellationToken);

            if (outcome.IsQueued)
            {
                queuedCount++;
            }
            else
            {
                skippedCount++;
                firstSkipReason ??= outcome.Reason;
            }
        }

        logger.LogInformation(
            "Relance d'impayés par SMS pour l'établissement {SchoolId} : {Queued} mis en file, {Skipped} écarté(s).",
            schoolId, queuedCount, skippedCount);

        return new DuesReminderSmsResult(queuedCount, skippedCount, firstSkipReason);
    }
}
