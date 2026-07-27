using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Notifications.Commands.SendDuesReminderSms;

/// <summary>
/// POST /sms/dues-reminders — relance par SMS les tuteurs des élèves dont la scolarité reste due.
/// Déclenchée à la main par le Directeur ou la Finance (aucun envoi de masse automatique : écrire à
/// toutes les familles d'un coup est une décision, pas un effet de bord).
///
/// Portée facultative par classe : relancer une classe entière est le geste courant en fin de mois ;
/// sans <see cref="ClassroomId"/>, la relance couvre tout l'établissement.
///
/// N'ÉCHOUE PAS si un SMS ne part pas : le résultat rapporte combien sont partis et combien ont été
/// écartés (sans numéro, solde épuisé…), ce qui laisse l'utilisateur décider de la suite.
/// </summary>
public record SendDuesReminderSmsCommand : IRequest<DuesReminderSmsResult>
{
    public Guid? ClassroomId { get; init; }
}

public record DuesReminderSmsResult(int SentCount, int SkippedCount, string? FirstSkipReason);

public class SendDuesReminderSmsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISmsDispatcher smsDispatcher,
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
        var debtors = await (
            from enrollment in dbContext.Enrollments.AsNoTracking()
            where enrollment.Status == EnrollmentStatus.Confirmed
                  && enrollment.AmountPaid < enrollment.TotalDue
                  && (request.ClassroomId == null || enrollment.ClassroomId == request.ClassroomId)
            join student in dbContext.Students.AsNoTracking() on enrollment.StudentId equals student.Id
            select new
            {
                student.Id,
                student.FullName,
                student.GuardianPhone,
                Remaining = enrollment.TotalDue - enrollment.AmountPaid
            })
            .ToListAsync(cancellationToken);

        var sentCount = 0;
        var skippedCount = 0;
        string? firstSkipReason = null;

        foreach (var debtor in debtors)
        {
            var body =
                $"Rappel : la scolarité de {debtor.FullName} présente un reliquat de "
                + $"{debtor.Remaining:N0} FCFA. Merci de régulariser auprès de l'établissement.";

            var outcome = await smsDispatcher.DispatchAsync(
                new SmsDispatchRequest(schoolId, debtor.GuardianPhone, body, SmsTrigger.DuesReminder, debtor.Id),
                cancellationToken);

            if (outcome.IsSent)
            {
                sentCount++;
            }
            else
            {
                skippedCount++;
                firstSkipReason ??= outcome.Reason;
            }
        }

        logger.LogInformation(
            "Relance d'impayés par SMS pour l'établissement {SchoolId} : {Sent} envoyé(s), {Skipped} écarté(s).",
            schoolId, sentCount, skippedCount);

        return new DuesReminderSmsResult(sentCount, skippedCount, firstSkipReason);
    }
}
