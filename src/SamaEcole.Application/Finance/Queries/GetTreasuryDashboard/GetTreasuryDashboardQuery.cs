using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;

/// <summary>
/// Tableau de bord Trésorerie — agrège les encaissements (<c>Payments</c>, module Caisse) et les
/// décaissements (<c>Disbursements</c>) déjà existants sur une période, sans nouvelle table : la
/// trésorerie n'est pas un troisième registre de mouvements, c'est une LECTURE combinée des deux
/// registres qui existent déjà. Écriture inchangée : on encaisse toujours via Caisse/Finance, on
/// décaisse toujours via Disbursements — ce tableau ne fait qu'additionner.
/// </summary>
public record GetTreasuryDashboardQuery(DateOnly? StartDate = null, DateOnly? EndDate = null)
    : IRequest<TreasuryDashboardDto>;

public record TreasuryBreakdownItem(string Label, decimal Amount);

/// <summary>Une ligne du fil d'activité récent, encaissement ou décaissement confondus, triés par date.</summary>
public record TreasuryTransactionItem(string Kind, string Label, decimal Amount, DateOnly Date);

public record TreasuryDashboardDto(
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalCollected,
    decimal TotalDisbursed,
    decimal NetPosition,
    decimal TotalOutstandingDebt,
    IReadOnlyList<TreasuryBreakdownItem> CollectedByMethod,
    IReadOnlyList<TreasuryBreakdownItem> DisbursedByCategory,
    IReadOnlyList<TreasuryTransactionItem> RecentTransactions);

public class GetTreasuryDashboardQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTreasuryDashboardQuery, TreasuryDashboardDto>
{
    private const int RecentTransactionsLimit = 20;

    public async Task<TreasuryDashboardDto> Handle(GetTreasuryDashboardQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = request.StartDate ?? new DateOnly(today.Year, today.Month, 1);
        var endDate = request.EndDate ?? today;

        // Bornes en DateTimeOffset pour Payments.PaidAt : la fin est EXCLUSIVE (lendemain minuit) pour
        // inclure toute la journée de fin, quelle que soit l'heure de l'encaissement.
        var startInclusive = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endExclusive = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue).AddDays(1), TimeSpan.Zero);

        // Payment.Status reflète le solde de l'INSCRIPTION après ce versement (Paid = soldée, Partial =
        // reste dû), pas si l'argent de CE versement a été reçu — il l'a toujours été dès l'insertion de
        // la ligne (RecordPaymentCommandHandler). Ne retenir que Status == Paid exclurait donc tous les
        // versements par mensualités du total encaissé : seul Cancelled (paiement annulé) doit sortir.
        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => p.Status != PaymentStatus.Cancelled && p.PaidAt >= startInclusive && p.PaidAt < endExclusive)
            .Select(p => new { p.Amount, p.Method, p.PaidAt, p.ReceiptNumber })
            .ToListAsync(cancellationToken);

        var disbursements = await dbContext.Disbursements.AsNoTracking()
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .Select(d => new { d.Amount, d.Category, d.Date, d.Reason })
            .ToListAsync(cancellationToken);

        // Reste à payer : solde COURANT des inscriptions confirmées, pas un flux de la période — une
        // photo de la créance encore due aujourd'hui, indépendante des dates de filtre ci-dessus.
        var totalOutstandingDebt = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Status == EnrollmentStatus.Confirmed)
            .SumAsync(e => e.TotalDue - e.AmountPaid, cancellationToken);

        var totalCollected = payments.Sum(p => p.Amount);
        var totalDisbursed = disbursements.Sum(d => d.Amount);

        var collectedByMethod = payments
            .GroupBy(p => p.Method.ToString())
            .Select(g => new TreasuryBreakdownItem(g.Key, g.Sum(p => p.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var disbursedByCategory = disbursements
            .GroupBy(d => d.Category.ToString())
            .Select(g => new TreasuryBreakdownItem(g.Key, g.Sum(d => d.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var recentTransactions = payments
            .Select(p => new TreasuryTransactionItem("Encaissement", p.ReceiptNumber, p.Amount, DateOnly.FromDateTime(p.PaidAt.UtcDateTime)))
            .Concat(disbursements.Select(d => new TreasuryTransactionItem("Décaissement", d.Reason, d.Amount, d.Date)))
            .OrderByDescending(t => t.Date)
            .Take(RecentTransactionsLimit)
            .ToList();

        return new TreasuryDashboardDto(
            startDate,
            endDate,
            totalCollected,
            totalDisbursed,
            totalCollected - totalDisbursed,
            totalOutstandingDebt,
            collectedByMethod,
            disbursedByCategory,
            recentTransactions);
    }
}
