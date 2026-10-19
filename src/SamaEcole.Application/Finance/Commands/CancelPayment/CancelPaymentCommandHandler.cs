using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.CancelPayment;

/// <summary>
/// Annule un versement saisi par erreur. Trois invariants :
///   • Règle #6 / #3 — le paiement n'est jamais supprimé ni réécrit : même montant, même reçu, même numéro.
///     Seuls son statut, le motif, la date et l'auteur de l'annulation sont posés.
///   • Règle #5 — le montant est retiré de <see cref="Domain.Entities.Enrollment.AmountPaid"/> par une écriture
///     sur l'inscription, donc sous son verrou optimiste xmin, exactement comme l'encaissement : une annulation
///     concurrente de la même ligne, ou un encaissement simultané, fait perdre le second et lui renvoie un 409
///     — jamais un solde faux. Le paiement lui-même ne porte pas de jeton, c'est l'inscription qui sérialise.
///   • Délai de grâce de 48 h (<see cref="PaymentCancellationPolicy"/>) : en deçà, l'annulation reste possible même
///     si la session de caisse est clôturée ; les états de caisse se recalculent alors sans ce versement. Au-delà,
///     l'annulation ordinaire est refusée (409) : seule la CONTRE-PASSATION, demandée explicitement et réservée au
///     Directeur, passe — le registre daté la comptabilise comme une écriture inverse datée du jour, sans réécrire
///     les périodes déjà arrêtées.
/// </summary>
public class CancelPaymentCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache)
    : IRequestHandler<CancelPaymentCommand, CancelPaymentResult>
{
    public async Task<CancelPaymentResult> Handle(CancelPaymentCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser le
        // paiement d'une autre école renvoie 404, jamais une annulation silencieuse.
        var payment = await dbContext.Payments
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Paiement {request.PaymentId} introuvable.");

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new BusinessRuleException(
                $"Le paiement {payment.ReceiptNumber} est déjà annulé.", "PAYMENT_ALREADY_CANCELLED");
        }

        // Délai de grâce (48 h après l'encaissement) : une erreur remarquée le lendemain reste corrigeable,
        // même si la session de caisse est déjà clôturée. Passé ce délai, seule la contre-passation (écriture
        // inverse datée du jour, voir PaymentLedger) est possible : explicite et réservée au Directeur.
        var now = timeProvider.GetUtcNow();
        var isReversal = PaymentCancellationPolicy.IsPastGracePeriod(payment.PaidAt, now);

        if (isReversal)
        {
            var hours = (int)PaymentCancellationPolicy.GracePeriod.TotalHours;

            if (!request.Reversal)
            {
                throw new BusinessRuleException(
                    $"Impossible d'annuler le paiement {payment.ReceiptNumber} : le délai d'annulation de {hours} heures "
                    + "après l'encaissement est dépassé. Le Directeur peut passer une contre-passation (écriture inverse datée d'aujourd'hui).",
                    "PAYMENT_CANCELLATION_WINDOW_EXPIRED");
            }

            if (currentUser.Role != Role.Directeur)
            {
                throw new UnauthorizedAccessException("Seul le Directeur peut passer une contre-passation.");
            }
        }

        // Inscription chargée SUIVIE : c'est son xmin qui sert de jeton optimiste au SaveChanges.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.Id == payment.EnrollmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {payment.EnrollmentId} introuvable.");

        // Session de caisse ouverte de l'auteur, s'il en a une : c'est là que l'annulation d'un versement d'une session
        // déjà clôturée sera comptée (écriture négative). Le Directeur n'en a pas toujours : dans ce cas l'annulation
        // figure dans les états par date, mais dans aucun écart de caisse.
        var cancellerSessionId = await dbContext.CashierSessions
            .Where(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        payment.Status = PaymentStatus.Cancelled;
        payment.CancelledAt = now;
        payment.CancelledByUserId = actorId;
        payment.CancellationReason = request.Reason.Trim();
        payment.CancellationOutcome = request.Outcome;
        payment.CancellationSessionId = cancellerSessionId;

        // Jamais négatif : un cumul déjà incohérent ne doit pas être aggravé par une annulation.
        enrollment.AmountPaid = Math.Max(0m, enrollment.AmountPaid - payment.Amount);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Après commit seulement, comme l'encaissement : le solde dû, le recouvrement et les effectifs
        // des deux dashboards ont changé.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return new CancelPaymentResult(
            payment.Id,
            payment.ReceiptNumber,
            payment.Amount,
            enrollment.TotalDue,
            enrollment.AmountPaid,
            enrollment.TotalDue - enrollment.AmountPaid,
            now,
            isReversal);
    }
}
