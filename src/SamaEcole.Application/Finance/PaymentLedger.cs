using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance;

/// <summary>
/// UNE ligne du registre des encaissements : soit un versement (montant positif), soit l'annulation d'un versement
/// antérieur (montant NÉGATIF, daté du jour de l'annulation).
/// </summary>
/// <param name="At">Date de l'écriture : <c>PaidAt</c> pour un versement, <c>CancelledAt</c> pour une annulation.</param>
/// <param name="PaidAt">Date d'encaissement du versement d'origine (identique à <paramref name="At"/> pour un versement).</param>
/// <param name="Amount">Signé : négatif pour une annulation.</param>
/// <param name="VatAmount">Signé comme <paramref name="Amount"/>.</param>
/// <param name="CancelledLater">Versement annulé APRÈS la fin de la période : il reste compté, mais signalé.</param>
public record PaymentLedgerEntry(
    Guid PaymentId,
    Guid EnrollmentId,
    Guid? CashierSessionId,
    string ReceiptNumber,
    PaymentMethod Method,
    PaymentCategory Category,
    decimal Amount,
    decimal VatAmount,
    DateTimeOffset At,
    DateTimeOffset PaidAt,
    bool IsReversal,
    bool CancelledLater,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    PaymentCancellationOutcome? Outcome)
{
    public bool IsCash => Method == PaymentMethod.Cash;

    /// <summary>
    /// Effet de cette écriture sur les ESPÈCES du tiroir-caisse (contrôle de caisse). Un versement en espèces en
    /// ajoute ; l'annulation d'un versement n'en retire QUE si l'argent est rendu au parent (<c>RefundedToParent</c>)
    /// — une simple correction ou une réutilisation pour l'inscription ne déplace aucune espèce (et un doublon de
    /// saisie n'a jamais représenté d'argent : l'écart du jour d'origine l'a déjà absorbé). Le total encaissé
    /// (chiffre d'affaires), lui, est net de TOUTES les annulations.
    /// </summary>
    public decimal CashEffect =>
        !IsCash ? 0m
        : !IsReversal ? Amount
        : Outcome == PaymentCancellationOutcome.RefundedToParent ? Amount
        : 0m;
}

/// <summary>
/// Registre daté des encaissements (spec 2026-10-17-deletion-dependency-guard-design.md §2.4) : ce qui permet de
/// corriger une erreur de caisse SANS réécrire une journée ou une période déjà arrêtée ni faire disparaître la trace.
///
/// Règle unique, appliquée à TOUS les états (journaux de caisse, trésorerie, TVA, tableaux de bord) :
///   • un versement compte dans la période de son encaissement ; s'il est annulé PLUS TARD, il y reste compté
///     (les chiffres d'hier restent ceux d'hier) et il est signalé « annulé le … » ;
///   • s'il est annulé dans la période même, il en sort (l'erreur et sa correction s'annulent) ;
///   • l'annulation d'un versement d'une période ANTÉRIEURE est une écriture négative, comptée dans la période où
///     l'annulation est faite.
/// Rien n'est supprimé : le paiement, son reçu, le motif, l'auteur et la destination de l'argent restent en base.
/// </summary>
public static class PaymentLedger
{
    /// <summary>Versements de la période [start, endExclusive) à compter (annulés plus tard inclus) et annulations négatives.</summary>
    public static async Task<List<PaymentLedgerEntry>> EntriesAsync(
        IApplicationDbContext dbContext, DateTimeOffset start, DateTimeOffset endExclusive, CancellationToken cancellationToken)
    {
        var counted = await dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt < endExclusive
                        && (p.Status != PaymentStatus.Cancelled || p.CancelledAt >= endExclusive))
            .ToListAsync(cancellationToken);

        var reversals = await dbContext.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Cancelled
                        && p.CancelledAt >= start && p.CancelledAt < endExclusive
                        && p.PaidAt < start)
            .ToListAsync(cancellationToken);

        return counted.Select(p => Positive(p, endExclusive))
            .Concat(reversals.Select(Reversal))
            .OrderBy(e => e.At)
            .ToList();
    }

    /// <summary>
    /// Total net de la période, en SQL (tableaux de bord) : versements comptés − annulations de versements antérieurs.
    /// <paramref name="selector"/> choisit la colonne sommée (montant ou TVA).
    /// </summary>
    public static async Task<decimal> NetAsync(
        IApplicationDbContext dbContext, DateTimeOffset start, DateTimeOffset endExclusive,
        System.Linq.Expressions.Expression<Func<Payment, decimal>> selector, CancellationToken cancellationToken)
    {
        var counted = await dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt < endExclusive
                        && (p.Status != PaymentStatus.Cancelled || p.CancelledAt >= endExclusive))
            .SumAsync(selector, cancellationToken);

        var reversed = await dbContext.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Cancelled
                        && p.CancelledAt >= start && p.CancelledAt < endExclusive
                        && p.PaidAt < start)
            .SumAsync(selector, cancellationToken);

        return counted - reversed;
    }

    /// <summary>
    /// Écritures d'une SESSION DE CAISSE : ses propres versements (ceux annulés avant sa clôture en sortent, ceux
    /// annulés après y restent, signalés) et les annulations comptées dans cette session pour des versements
    /// d'une session déjà clôturée au moment de l'annulation.
    /// </summary>
    public static async Task<List<PaymentLedgerEntry>> SessionEntriesAsync(
        IApplicationDbContext dbContext, CashierSession session, CancellationToken cancellationToken)
    {
        var closedAt = session.ClosedAt;

        var own = await dbContext.Payments.AsNoTracking()
            .Where(p => p.CashierSessionId == session.Id
                        && (p.Status != PaymentStatus.Cancelled
                            || (closedAt != null && p.CancelledAt > closedAt)))
            .ToListAsync(cancellationToken);

        var reversals = await (
            from p in dbContext.Payments.AsNoTracking()
            join origin in dbContext.CashierSessions.AsNoTracking() on p.CashierSessionId equals origin.Id
            where p.Status == PaymentStatus.Cancelled
                  && p.CancellationSessionId == session.Id
                  && origin.Id != session.Id
                  && origin.ClosedAt != null && origin.ClosedAt < p.CancelledAt
            select p)
            .ToListAsync(cancellationToken);

        return own.Select(p => PositiveInSession(p, closedAt))
            .Concat(reversals.Select(Reversal))
            .OrderBy(e => e.At)
            .ToList();
    }

    private static PaymentLedgerEntry Positive(Payment p, DateTimeOffset endExclusive) => Build(
        p, p.Amount, p.VatAmount, p.PaidAt, isReversal: false,
        cancelledLater: p.Status == PaymentStatus.Cancelled && p.CancelledAt >= endExclusive);

    private static PaymentLedgerEntry PositiveInSession(Payment p, DateTimeOffset? closedAt) => Build(
        p, p.Amount, p.VatAmount, p.PaidAt, isReversal: false,
        cancelledLater: p.Status == PaymentStatus.Cancelled && closedAt != null && p.CancelledAt > closedAt);

    private static PaymentLedgerEntry Reversal(Payment p) => Build(
        p, -p.Amount, -p.VatAmount, p.CancelledAt!.Value, isReversal: true, cancelledLater: false);

    private static PaymentLedgerEntry Build(
        Payment p, decimal amount, decimal vat, DateTimeOffset at, bool isReversal, bool cancelledLater) => new(
        p.Id, p.EnrollmentId, p.CashierSessionId, p.ReceiptNumber, p.Method, p.Category,
        amount, vat, at, p.PaidAt, isReversal, cancelledLater, p.CancelledAt, p.CancellationReason, p.CancellationOutcome);
}
