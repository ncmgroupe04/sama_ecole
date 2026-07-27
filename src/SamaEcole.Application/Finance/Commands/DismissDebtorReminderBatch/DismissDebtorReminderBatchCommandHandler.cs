using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using BusinessRuleException = SamaEcole.Application.Common.Exceptions.BusinessRuleException;

namespace SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;

public class DismissDebtorReminderBatchCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<DismissDebtorReminderBatchCommand>
{
    public async Task Handle(DismissDebtorReminderBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await dbContext.DebtorReminderBatches
            .SingleOrDefaultAsync(b => b.Id == request.BatchId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot de relance introuvable : {request.BatchId}");

        if (batch.Status != DebtorReminderBatchStatus.Draft)
        {
            throw new BusinessRuleException(
                "Ce lot de relance a déjà été envoyé ou écarté : impossible de l'écarter une seconde fois.");
        }

        batch.Status = DebtorReminderBatchStatus.Dismissed;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
