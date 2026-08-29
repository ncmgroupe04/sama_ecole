using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetCurrentCashierSession;

/// <summary>
/// GET /finance/sessions/current — la session de caisse OUVERTE de l'utilisateur courant, ou <c>null</c>
/// s'il n'en a aucune. Volume 1 §15.1 : la journée d'encaissement s'ouvre par une session de caisse ;
/// <see cref="Application.Finance.Commands.RecordPayment.RecordPaymentCommandHandler"/> refuse déjà tout
/// encaissement hors d'une session ouverte (422 « Aucune session de caisse ouverte pour cet
/// utilisateur. ») — cette requête est ce qui permet à l'écran /caisse de savoir, AVANT toute tentative
/// d'encaissement, s'il doit proposer d'ouvrir une session ou le statut de celle déjà ouverte.
/// </summary>
public record GetCurrentCashierSessionQuery : IRequest<CurrentCashierSessionDto?>;

public record CurrentCashierSessionDto(
    Guid SessionId,
    decimal OpeningBalance,
    DateTimeOffset OpenedAt,
    decimal TotalCollected,
    int PaymentsCount);

public class GetCurrentCashierSessionQueryHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<GetCurrentCashierSessionQuery, CurrentCashierSessionDto?>
{
    public async Task<CurrentCashierSessionDto?> Handle(
        GetCurrentCashierSessionQuery request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Global Query Filter + RLS bornent déjà à l'école courante (AGENTS.md règle #2) : pas de
        // filtre SchoolId explicite ici.
        var session = await dbContext.CashierSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open, cancellationToken);

        if (session is null)
        {
            return null;
        }

        // Même agrégat que CloseCashierSessionCommandHandler (paiements annulés exclus, Volume 1 §15.2) :
        // afficher un total différent de celui que la clôture calculerait romprait la confiance dans
        // le chiffre montré pendant la journée.
        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => p.CashierSessionId == session.Id && p.Status != PaymentStatus.Cancelled)
            .Select(p => p.Amount)
            .ToListAsync(cancellationToken);

        return new CurrentCashierSessionDto(
            session.Id,
            session.OpeningBalance,
            session.OpenedAt,
            payments.Sum(),
            payments.Count);
    }
}
