using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;

public record DailyClosingReportDto(
    string SchoolName,
    string SchoolAddress,
    string SchoolLogoUrl,
    DateTime Date,
    string SessionId,
    string CashierName,
    string TimeRange,
    decimal OpeningBalance,
    decimal TotalCollected,
    decimal TotalCashInRegister,
    List<PaymentMethodBreakdownDto> MethodBreakdowns,
    List<FeeCategoryBreakdownDto> CategoryBreakdowns,
    List<TransactionRowDto> Transactions,
    // Ticket JGK-F09 — nuls tant que la session n'est pas clôturée (rapport consultable, en théorie,
    // sur une session encore ouverte) ; toujours renseignés dès CloseCashierSessionCommandHandler passé.
    decimal? ActualCashAmount = null,
    decimal? DiscrepancyAmount = null,
    string? DiscrepancyReason = null
);

public record PaymentMethodBreakdownDto(PaymentMethod Method, decimal Amount);
public record FeeCategoryBreakdownDto(string CategoryName, decimal Amount);
public record TransactionRowDto(string Time, string ReceiptNumber, string StudentName, string Category, string Method, decimal Amount);
