using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BusinessRuleException = SamaEcole.Application.Common.Exceptions.BusinessRuleException;

namespace SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;

public class SendDebtorReminderBatchCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    ISmsDispatcher smsDispatcher,
    TimeProvider timeProvider,
    ILogger<SendDebtorReminderBatchCommandHandler> logger)
    : IRequestHandler<SendDebtorReminderBatchCommand, SendDebtorReminderBatchResult>
{
    public async Task<SendDebtorReminderBatchResult> Handle(
        SendDebtorReminderBatchCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter borne déjà à l'école courante : un lot d'une autre école est
        // structurellement introuvable ici.
        var batch = await dbContext.DebtorReminderBatches
            .SingleOrDefaultAsync(b => b.Id == request.BatchId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot de relance introuvable : {request.BatchId}");

        if (batch.Status != DebtorReminderBatchStatus.Draft)
        {
            throw new BusinessRuleException(
                "Ce lot de relance a déjà été envoyé ou écarté : impossible de l'envoyer une seconde fois.");
        }

        var items = await (
            from i in dbContext.DebtorReminderBatchItems
            where i.DebtorReminderBatchId == batch.Id
            join s in dbContext.Students.AsNoTracking() on i.StudentId equals s.Id
            select new { Item = i, s.FullName })
            .ToListAsync(cancellationToken);

        var queuedCount = 0;
        var skippedCount = 0;
        string? firstSkipReason = null;

        foreach (var row in items)
        {
            var body =
                $"Rappel : la scolarité de {row.FullName} présente un reliquat de "
                + $"{row.Item.RemainingBalance:N0} FCFA, en retard de {row.Item.DaysOverdue} jour(s). "
                + "Merci de régulariser auprès de l'établissement.";

            var outcome = await smsDispatcher.DispatchAsync(
                new SmsDispatchRequest(schoolId, row.Item.GuardianPhone, body, SmsTrigger.DuesReminder, row.Item.StudentId),
                cancellationToken);

            row.Item.SmsMessageId = outcome.MessageId;

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

        batch.Status = DebtorReminderBatchStatus.Sent;
        batch.SentByUserId = actorId;
        batch.SentAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Lot de relance {BatchId} envoyé pour l'établissement {SchoolId} : {Queued} mis en file, {Skipped} écarté(s).",
            batch.Id, schoolId, queuedCount, skippedCount);

        return new SendDebtorReminderBatchResult(queuedCount, skippedCount, firstSkipReason);
    }
}
