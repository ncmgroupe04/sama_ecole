using System.Globalization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
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
    ISmsDispatcher smsDispatcher,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache)
    : IRequestHandler<RecordPaymentCommand, RecordPaymentResult>
{
    public async Task<RecordPaymentResult> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var result = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            // Idempotence côté client (ticket JGK-L01) : un retry après coupure réseau, MÊME clé,
            // rejoue le résultat déjà produit plutôt que de recréer un paiement ou d'échouer en 409 —
            // le caissier qui n'a jamais vu la confirmation du premier essai doit quand même récupérer
            // son numéro de reçu, pas juste un message d'erreur. Vérifié avant tout le reste : même une
            // session de caisse depuis fermée ne doit pas empêcher de rejouer un paiement déjà réussi.
            if (request.IdempotencyKey is { } idempotencyKey)
            {
                var existing = await dbContext.Payments.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.SchoolId == schoolId && p.IdempotencyKey == idempotencyKey, ct);

                if (existing is not null)
                {
                    var currentTotalDue = await dbContext.Enrollments.AsNoTracking()
                        .Where(e => e.Id == existing.EnrollmentId)
                        .Select(e => e.TotalDue)
                        .FirstAsync(ct);

                    return new RecordPaymentResult(
                        existing.Id,
                        existing.ReceiptNumber,
                        existing.Amount,
                        currentTotalDue,
                        currentTotalDue - existing.BalanceAfter,
                        existing.BalanceAfter,
                        existing.Status.ToString());
                }
            }

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
                VatRate = request.VatRate,
                VatAmount = VatCalculator.ComputeVatAmount(request.Amount, request.VatRate),
                Method = request.Method,
                Status = status,
                BalanceAfter = enrollment.TotalDue - newAmountPaid,
                ReceiptNumber = receiptNumber,
                IdempotencyKey = request.IdempotencyKey,
                ReceivedByUserId = actorId,
                PaidAt = timeProvider.GetUtcNow()
            };

            if (request.Breakdowns != null && request.Breakdowns.Any())
            {
                foreach (var breakdown in request.Breakdowns)
                {
                    payment.Breakdowns.Add(new PaymentBreakdown
                    {
                        // SchoolId explicite, comme sur le Payment lui-même : payment_breakdowns est une
                        // table tenant sous RLS PostgreSQL (AGENTS.md règle #2). Sans lui, l'INSERT de
                        // la ligne enfant viole la policy (« new row violates row-level security
                        // policy ») — le SchoolId du parent n'est pas propagé par EF Core.
                        SchoolId = schoolId,
                        FeeCategoryId = breakdown.FeeCategoryId,
                        AmountAllocated = breakdown.AmountAllocated,
                        // Vide normalisé à null : une chaîne blanche ferait échouer le repli sur
                        // ReferencePeriod côté reçu et imprimerait une cellule vide sans raison.
                        Label = string.IsNullOrWhiteSpace(breakdown.Label) ? null : breakdown.Label.Trim()
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

        // Après commit seulement : un versement qui roll back ne doit pas invalider un cache qui restait
        // pourtant correct. Un cache miss superflu est sans conséquence, contrairement à l'inverse.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        await NotifyGuardianAsync(schoolId, request.EnrollmentId, result, cancellationToken);

        return result;
    }

    /// <summary>
    /// Confirmation du versement au tuteur, APRÈS le commit — un SMS annonçant un encaissement que la
    /// transaction annulerait ensuite serait un faux reçu. Même raisonnement que l'e-mail
    /// d'activation d'abonnement, envoyé lui aussi hors transaction.
    ///
    /// N'échoue jamais l'encaissement : SmsDispatcher applique toutes les gardes (formule,
    /// commutateur de l'école, solde, historique) et ne lève pas. Une caisse ne doit pas se bloquer
    /// parce qu'un opérateur télécom est injoignable.
    /// </summary>
    private async Task NotifyGuardianAsync(
        Guid schoolId, Guid enrollmentId, RecordPaymentResult result, CancellationToken cancellationToken)
    {
        var guardian = await (
            from enrollment in dbContext.Enrollments.AsNoTracking()
            where enrollment.Id == enrollmentId
            join student in dbContext.Students.AsNoTracking() on enrollment.StudentId equals student.Id
            select new { student.Id, student.FullName, student.GuardianPhone })
            .FirstOrDefaultAsync(cancellationToken);

        if (guardian is null)
        {
            return;
        }

        var body =
            $"Paiement reçu : {FormatMoney(result.Amount)} FCFA pour {guardian.FullName} "
            + $"(reçu n° {result.ReceiptNumber}). Reste dû : {FormatMoney(result.RemainingBalance)} FCFA. "
            + "Merci de conserver votre reçu.";

        await smsDispatcher.DispatchAsync(
            new SmsDispatchRequest(schoolId, guardian.GuardianPhone, body, SmsTrigger.PaymentReceipt, guardian.Id),
            cancellationToken);
    }

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — comme sur le reçu.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");
}
