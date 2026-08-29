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

        var payments = await dbContext.Payments.AsNoTracking()
            .Include(p => p.Breakdowns)
            .ThenInclude(b => b.FeeCategory)
            .Include(p => p.Enrollment)
            .Where(p => p.CashierSessionId == session.Id && p.Status != PaymentStatus.Cancelled)
            .OrderBy(p => p.PaidAt)
            .ToListAsync(cancellationToken);

        var studentIds = payments.Select(p => p.Enrollment.StudentId).Distinct().ToList();
        var students = await dbContext.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, cancellationToken);

        var totalCollected = payments.Sum(p => p.Amount);
        
        var methodBreakdowns = payments
            .GroupBy(p => p.Method)
            .Select(g => new PaymentMethodBreakdownDto(g.Key, g.Sum(p => p.Amount)))
            .ToList();

        // Calculate Category Breakdown
        // If a payment has breakdowns, we use them. Otherwise, we put the total amount in the main Category.
        var categoryAmounts = new Dictionary<string, decimal>();
        foreach(var payment in payments)
        {
            if (payment.Breakdowns.Any())
            {
                foreach(var b in payment.Breakdowns)
                {
                    var catName = b.FeeCategory.Name;
                    categoryAmounts[catName] = categoryAmounts.GetValueOrDefault(catName) + b.AmountAllocated;
                }
            }
            else
            {
                var catName = payment.Category.ToString();
                categoryAmounts[catName] = categoryAmounts.GetValueOrDefault(catName) + payment.Amount;
            }
        }

        var categoryBreakdowns = categoryAmounts
            .Select(kvp => new FeeCategoryBreakdownDto(kvp.Key, kvp.Value))
            .ToList();

        var transactions = payments.Select(p => new TransactionRowDto(
            Time: p.PaidAt.ToString("HH:mm"),
            ReceiptNumber: p.ReceiptNumber,
            StudentName: students.GetValueOrDefault(p.Enrollment.StudentId, "Inconnu"),
            Category: p.Category.ToString(),
            Method: p.Method.ToString(),
            Amount: p.Amount
        )).ToList();

        var timeRange = $"{session.OpenedAt:HH:mm} - {(session.ClosedAt.HasValue ? session.ClosedAt.Value.ToString("HH:mm") : "En cours")}";

        var dto = new DailyClosingReportDto(
            SchoolName: school.Name,
            SchoolAddress: school.Address ?? string.Empty,
            SchoolLogoUrl: school.LogoUrl ?? string.Empty,
            Date: session.OpenedAt.Date,
            SessionId: $"SES-{session.OpenedAt:yyyy-MMdd}-{session.Id.ToString().Substring(0, 4).ToUpper()}",
            CashierName: session.Cashier.FullName,
            TimeRange: timeRange,
            OpeningBalance: session.OpeningBalance,
            TotalCollected: totalCollected,
            TotalCashInRegister: session.OpeningBalance + methodBreakdowns.FirstOrDefault(m => m.Method == PaymentMethod.Cash)?.Amount ?? 0,
            MethodBreakdowns: methodBreakdowns,
            CategoryBreakdowns: categoryBreakdowns,
            Transactions: transactions,
            ActualCashAmount: session.ActualCashAmount,
            DiscrepancyAmount: session.DiscrepancyAmount,
            DiscrepancyReason: session.DiscrepancyReason
        );

        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);
        var pdfBytes = pdfGenerator.Generate(dto, logo);

        var fileName = $"Rapport_Caisse_{dto.Date:yyyyMMdd}_{dto.SessionId}.pdf";

        return new ReceiptPdfResult(pdfBytes, fileName);
    }
}
