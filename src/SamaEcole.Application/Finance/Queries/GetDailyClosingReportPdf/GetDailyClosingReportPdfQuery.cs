using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;

public record GetDailyClosingReportPdfQuery(Guid SessionId) : IRequest<ReceiptPdfResult>, IAuditableRequest;

public record ReceiptPdfResult(byte[] Content, string FileName);

public class GetDailyClosingReportPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISchoolLogoProvider logoProvider,
    IDailyClosingReportPdfGenerator pdfGenerator)
    : IRequestHandler<GetDailyClosingReportPdfQuery, ReceiptPdfResult>
{
    public async Task<ReceiptPdfResult> Handle(GetDailyClosingReportPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Ecole {schoolId} introuvable.");

        var session = await dbContext.CashierSessions.AsNoTracking()
            .Include(s => s.Cashier)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session {request.SessionId} introuvable.");

        // Caissier désactivé/supprimé depuis la clôture : la nav peut être nulle. Le rapport reste
        // émissible — un « — » vaut mieux qu'un NullReferenceException transformé en 500 muet.
        var cashierName = session.Cashier?.FullName ?? "—";

        // Registre de la session (PaymentLedger) : ses versements — y compris ceux annulés APRÈS la clôture, que le journal
        // signale sans les retirer (les chiffres signés ce jour-là ne bougent pas) — et les annulations comptées dans
        // cette session pour des versements d'une session antérieure, en négatif.
        var entries = await PaymentLedger.SessionEntriesAsync(dbContext, session, cancellationToken);

        var paymentIds = entries.Select(e => e.PaymentId).Distinct().ToList();
        var payments = await dbContext.Payments.AsNoTracking()
            .Include(p => p.Breakdowns)
            .ThenInclude(b => b.FeeCategory)
            .Where(p => paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // L'élève d'un versement annulé peut avoir été supprimé depuis : son nom reste imprimé, suivi de
        // « (élève supprimé) » (PaymentOwnerResolver) — jamais une inscription nulle qui ferait planter le rapport.
        var owners = await PaymentOwnerResolver.ResolveAsync(
            dbContext, schoolId, payments.Values.Select(p => p.EnrollmentId).Distinct().ToList(), cancellationToken);

        string StudentOf(Guid paymentId) =>
            owners.TryGetValue(payments[paymentId].EnrollmentId, out var owner) ? owner.DisplayName : "Inconnu";

        // Montants SIGNÉS : le total, la répartition par mode et par catégorie sont NETS des annulations, donc
        // identiques à ce que la clôture a enregistré (CloseCashierSessionCommandHandler utilise le même registre).
        var totalCollected = entries.Sum(e => e.Amount);
        var totalReversed = -entries.Where(e => e.IsReversal).Sum(e => e.Amount);

        var methodBreakdowns = entries
            .GroupBy(e => e.Method)
            .Select(g => new PaymentMethodBreakdownDto(g.Key, g.Sum(e => e.Amount)))
            .ToList();

        // Répartition par catégorie : les lignes de ventilation du versement si elles existent, sinon sa catégorie
        // principale ; une annulation reprend les mêmes lignes avec le signe opposé.
        var categoryAmounts = new Dictionary<string, decimal>();
        foreach (var entry in entries)
        {
            var payment = payments[entry.PaymentId];
            var sign = entry.IsReversal ? -1m : 1m;

            if (payment.Breakdowns.Any())
            {
                foreach (var b in payment.Breakdowns)
                {
                    var catName = b.FeeCategory.Name;
                    categoryAmounts[catName] = categoryAmounts.GetValueOrDefault(catName) + sign * b.AmountAllocated;
                }
            }
            else
            {
                var catName = payment.Category.ToString();
                categoryAmounts[catName] = categoryAmounts.GetValueOrDefault(catName) + sign * payment.Amount;
            }
        }

        var categoryBreakdowns = categoryAmounts
            .Select(kvp => new FeeCategoryBreakdownDto(kvp.Key, kvp.Value))
            .ToList();

        var transactions = entries.Where(e => !e.IsReversal).Select(e => new TransactionRowDto(
            Time: e.At.ToString("HH:mm"),
            ReceiptNumber: e.ReceiptNumber,
            StudentName: StudentOf(e.PaymentId),
            Category: e.Category.ToString(),
            Method: e.Method.ToString(),
            Amount: e.Amount,
            CancelledNote: e.CancelledLater
                ? PaymentCancellationOutcomeLabels.CancelledNote(e.CancelledAt, e.CancellationReason, e.Outcome)
                : null
        )).ToList();

        var reversals = entries.Where(e => e.IsReversal).Select(e => new ReversalRowDto(
            Time: e.At.ToString("dd/MM HH:mm"),
            ReceiptNumber: e.ReceiptNumber,
            StudentName: StudentOf(e.PaymentId),
            Method: e.Method.ToString(),
            Amount: e.Amount,
            Note: $"Annulation du reçu encaissé le {e.PaidAt:dd/MM/yyyy} — "
                  + $"{(string.IsNullOrWhiteSpace(e.CancellationReason) ? "sans motif" : e.CancellationReason)} "
                  + $"({PaymentCancellationOutcomeLabels.Label(e.Outcome)})"
        )).ToList();

        var timeRange = $"{session.OpenedAt:HH:mm} - {(session.ClosedAt.HasValue ? session.ClosedAt.Value.ToString("HH:mm") : "En cours")}";

        var dto = new DailyClosingReportDto(
            SchoolName: school.Name,
            SchoolAddress: school.Address ?? string.Empty,
            SchoolLogoUrl: school.LogoUrl ?? string.Empty,
            Date: session.OpenedAt.Date,
            SessionId: $"SES-{session.OpenedAt:yyyy-MMdd}-{session.Id.ToString().Substring(0, 4).ToUpper()}",
            CashierName: cashierName,
            TimeRange: timeRange,
            OpeningBalance: session.OpeningBalance,
            TotalCollected: totalCollected,
            // Espèces attendues : seule une annulation REMBOURSÉE au parent en retire (PaymentLedgerEntry.CashEffect).
            TotalCashInRegister: session.OpeningBalance + entries.Sum(e => e.CashEffect),
            MethodBreakdowns: methodBreakdowns,
            CategoryBreakdowns: categoryBreakdowns,
            Transactions: transactions,
            ActualCashAmount: session.ActualCashAmount,
            DiscrepancyAmount: session.DiscrepancyAmount,
            DiscrepancyReason: session.DiscrepancyReason,
            Reversals: reversals,
            TotalReversed: totalReversed
        );

        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);
        var pdfBytes = pdfGenerator.Generate(dto, logo);

        var fileName = $"Rapport_Caisse_{dto.Date:yyyyMMdd}_{dto.SessionId}.pdf";

        return new ReceiptPdfResult(pdfBytes, fileName);
    }
}
