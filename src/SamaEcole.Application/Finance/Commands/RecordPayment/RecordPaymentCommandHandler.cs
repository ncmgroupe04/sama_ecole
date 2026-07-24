using System.Globalization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.RecordPayment;

/// <summary>
/// Cœur du ticket JGK-F02. Encaisse un versement en respectant trois invariants :
///   • Règle #4 — <c>TotalDue</c> n'est jamais touché ; on n'incrémente que le cumul encaissé
///     (<see cref="Enrollment.AmountPaid"/>). La Finance impute, elle ne fixe pas le dû.
///   • Règle #5 — deux encaissements CONCURRENTS sur le même solde ne peuvent pas s'écraser : l'écriture
///     de <c>AmountPaid</c> sur l'inscription passe par le verrou optimiste xmin ; le perdant reçoit un
///     409 (ConcurrencyConflictException), jamais un sur-crédit silencieux. C'est le critère obligatoire.
///   • Règle #3 — le numéro de reçu officiel est attribué DANS la transaction, gapless, comme le matricule.
///
/// Tout est atomique : si quoi que ce soit échoue après l'attribution du numéro de reçu, celui-ci est
/// rembobiné avec la transaction — aucun trou dans la numérotation, aucun paiement orphelin.
/// </summary>
public class RecordPaymentCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    IMatriculeGenerator matriculeGenerator,
    TimeProvider timeProvider)
    : IRequestHandler<RecordPaymentCommand, RecordPaymentResult>
{
    public async Task<RecordPaymentResult> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var activeSession = await dbContext.CashierSessions
                .FirstOrDefaultAsync(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open, ct);

            if (activeSession == null)
            {
                throw new ValidationException([
                    new ValidationFailure("CashierSession", "Aucune session de caisse ouverte pour cet utilisateur.")
                ]);
            }

            // Inscription chargée SUIVIE (pas AsNoTracking) : c'est le xmin lu ici qui sert de jeton
            // optimiste au SaveChanges. Le Global Query Filter + la RLS bornent à l'école courante :
            // encaisser sur une inscription d'une autre école renvoie 404, jamais un versement silencieux.
            var enrollment = await dbContext.Enrollments
                .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId, ct)
                ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

            if (enrollment.Status == EnrollmentStatus.Cancelled)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.EnrollmentId),
                        "Impossible d'encaisser sur une inscription annulée.")
                ]);
            }

            var remaining = enrollment.TotalDue - enrollment.AmountPaid;

            if (remaining <= 0)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.Amount), "Cette inscription est déjà soldée.")
                ]);
            }

            if (request.Amount > remaining)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.Amount),
                        $"Le montant dépasse le solde restant ({FormatMoney(remaining)} FCFA).")
                ]);
            }

            // Numéro de reçu officiel, attribué DANS la transaction (règle #3), depuis le même registre
            // gapless par établissement que le reçu d'inscription (JGK-E02).
            var receiptNumber = await matriculeGenerator.GenerateNextReceiptNumberAsync(schoolId, ct);

            var newAmountPaid = enrollment.AmountPaid + request.Amount;
            var status = newAmountPaid >= enrollment.TotalDue ? PaymentStatus.Paid : PaymentStatus.Partial;

            var payment = new Payment
            {
                SchoolId = schoolId,
                EnrollmentId = enrollment.Id,
                CashierSessionId = activeSession.Id,
                Category = request.Category,
                ReferencePeriod = request.ReferencePeriod,
                Amount = request.Amount,
                Method = request.Method,
                Status = status,
                BalanceAfter = enrollment.TotalDue - newAmountPaid,
                ReceiptNumber = receiptNumber,
                ReceivedByUserId = actorId,
                PaidAt = timeProvider.GetUtcNow()
            };

            if (request.Breakdowns != null && request.Breakdowns.Any())
            {
                foreach (var breakdown in request.Breakdowns)
                {
                    payment.Breakdowns.Add(new PaymentBreakdown
                    {
                        FeeCategoryId = breakdown.FeeCategoryId,
                        AmountAllocated = breakdown.AmountAllocated
                    });
                }
            }

            dbContext.Payments.Add(payment);

            // L'écriture qui compte pour la concurrence : UPDATE enrollments ... WHERE xmin = <valeur lue>.
            // Deux encaissements concurrents ont lu le même xmin ; le second UPDATE ne touchera aucune
            // ligne et SaveChanges lèvera une ConcurrencyConflictException -> 409 (règle #5).
            enrollment.AmountPaid = newAmountPaid;

            await dbContext.SaveChangesAsync(ct);

            return new RecordPaymentResult(
                payment.Id,
                receiptNumber,
                request.Amount,
                enrollment.TotalDue,
                newAmountPaid,
                enrollment.TotalDue - newAmountPaid,
                status.ToString());
        }, cancellationToken);
    }

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — comme sur le reçu.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");
}
