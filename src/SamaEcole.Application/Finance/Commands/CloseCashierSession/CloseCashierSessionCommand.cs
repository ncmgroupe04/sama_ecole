using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.CloseCashierSession;

/// <summary>
/// Ticket JGK-F09 — le comptage physique du numéraire devient une étape obligatoire de la clôture,
/// pas une vérification annexe : <see cref="ActualCashAmount"/> est un paramètre requis de la commande
/// elle-même, jamais une valeur qu'on pourrait omettre. <see cref="DiscrepancyReason"/> reste facultatif
/// AU NIVEAU DU TYPE — c'est le Handler qui le rend obligatoire, mais seulement quand un écart existe
/// réellement (voir plus bas) : imposer le motif inconditionnellement forcerait une phrase creuse
/// (« RAS ») sur la majorité des clôtures, qui tombent juste.
/// </summary>
public record CloseCashierSessionCommand(
    Guid SessionId,
    decimal ActualCashAmount,
    string? DiscrepancyReason = null) : IRequest<CloseCashierSessionResult>, IAuditableRequest;

public record CloseCashierSessionResult(
    Guid SessionId,
    decimal OpeningBalance,
    decimal TotalCollected,
    decimal ExpectedClosingBalance,
    decimal ExpectedCashAmount,
    decimal ActualCashAmount,
    decimal DiscrepancyAmount,
    string? DiscrepancyReason);

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

        var payments = await dbContext.Payments
            .Where(p => p.CashierSessionId == session.Id && p.Status != PaymentStatus.Cancelled)
            .Select(p => new { p.Amount, p.Method })
            .ToListAsync(cancellationToken);

        var totalCollected = payments.Sum(p => p.Amount);
        var expectedClosingBalance = session.OpeningBalance + totalCollected;

        // Écart de caisse (JGK-F09) : comparé aux ESPÈCES seulement — un virement, un chèque ou un
        // versement mobile money n'a jamais transité par le tiroir-caisse physique, donc ne peut
        // jamais participer à un manquant ou un surplus constaté au comptage. Comparer ActualCashAmount
        // à expectedClosingBalance (toutes méthodes) produirait un écart systématique et sans rapport
        // avec la réalité dès qu'une école accepte Wave/Orange Money/virement en plus des espèces —
        // même distinction que report.TotalCashInRegister sur le rapport PDF existant.
        var cashCollected = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        var expectedCashAmount = session.OpeningBalance + cashCollected;
        var discrepancy = request.ActualCashAmount - expectedCashAmount;

        string? discrepancyReason = null;
        if (discrepancy != 0)
        {
            if (string.IsNullOrWhiteSpace(request.DiscrepancyReason))
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.DiscrepancyReason),
                        "Un écart de caisse a été constaté : justifiez-le avant de pouvoir clôturer la session.")
                ]);
            }

            discrepancyReason = request.DiscrepancyReason.Trim();
        }

        session.ClosedAt = timeProvider.GetUtcNow();
        session.Status = CashierSessionStatus.Closed;
        session.ClosingBalance = expectedClosingBalance;
        session.ExpectedCashAmount = expectedCashAmount;
        session.ActualCashAmount = request.ActualCashAmount;
        session.DiscrepancyAmount = discrepancy;
        session.DiscrepancyReason = discrepancyReason;

        // Rien d'autre à écrire : la session elle-même, une fois close, ne se rouvre ni ne se
        // re-clôture jamais (garde ci-dessus) — ces colonnes SONT l'écriture d'ajustement historisée
        // qu'exige le ticket, pas une table séparée. IAuditableRequest journalise en plus qui a clôturé,
        // quand, et avec quelles valeurs (AGENTS.md, JGK-H01).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CloseCashierSessionResult(
            session.Id, session.OpeningBalance, totalCollected, expectedClosingBalance,
            expectedCashAmount, request.ActualCashAmount, discrepancy, discrepancyReason);
    }
}
