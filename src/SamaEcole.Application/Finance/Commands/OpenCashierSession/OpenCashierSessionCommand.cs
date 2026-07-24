using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.OpenCashierSession;

public record OpenCashierSessionCommand(decimal OpeningBalance) : IRequest<Guid>, IAuditableRequest;

public class OpenCashierSessionCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<OpenCashierSessionCommand, Guid>
{
    public async Task<Guid> Handle(OpenCashierSessionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var activeSession = await dbContext.CashierSessions
            .FirstOrDefaultAsync(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open, cancellationToken);

        if (activeSession != null)
        {
            throw new ValidationException([
                new ValidationFailure("CashierId", "Cet utilisateur a déjà une session de caisse ouverte.")
            ]);
        }

        var session = new CashierSession
        {
            SchoolId = schoolId,
            CashierId = actorId,
            OpenedAt = timeProvider.GetUtcNow(),
            OpeningBalance = request.OpeningBalance,
            Status = CashierSessionStatus.Open
        };

        dbContext.CashierSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return session.Id;
    }
}
