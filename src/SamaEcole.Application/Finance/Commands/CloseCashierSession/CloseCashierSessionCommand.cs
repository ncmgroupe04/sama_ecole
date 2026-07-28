using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.CloseCashierSession;

public record CloseCashierSessionCommand(Guid SessionId) : IRequest<CloseCashierSessionResult>, IAuditableRequest;

public record CloseCashierSessionResult(Guid SessionId, decimal OpeningBalance, decimal TotalCollected, decimal ExpectedClosingBalance);

public class CloseCashierSessionCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<CloseCashierSessionCommand, CloseCashierSessionResult>
{
    public async Task<CloseCashierSessionResult> Handle(CloseCashierSessionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var session = await dbContext.CashierSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session {request.SessionId} introuvable.");

        if (session.Status != CashierSessionStatus.Open)
        {
            throw new ValidationException([
                new ValidationFailure("SessionId", "Cette session est déjà fermée ou vérifiée.")
            ]);
        }

        var paymentsAmount = await dbContext.Payments
            .Where(p => p.CashierSessionId == session.Id && p.Status != PaymentStatus.Cancelled)
            .SumAsync(p => p.Amount, cancellationToken);

        session.ClosedAt = timeProvider.GetUtcNow();
        session.Status = CashierSessionStatus.Closed;
        session.ClosingBalance = session.OpeningBalance + paymentsAmount;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CloseCashierSessionResult(session.Id, session.OpeningBalance, paymentsAmount, session.ClosingBalance.Value);
    }
}
