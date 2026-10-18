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
    string? DiscrepancyReason = null,
    // Annulations comptées dans CETTE session pour des versements d'une session déjà clôturée (écritures négatives,
    // PaymentLedger). TotalCollected est déjà NET de ces lignes ; TotalReversed en est la part, en valeur positive.
    List<ReversalRowDto>? Reversals = null,
    decimal TotalReversed = 0m
);

public record PaymentMethodBreakdownDto(PaymentMethod Method, decimal Amount);
public record FeeCategoryBreakdownDto(string CategoryName, decimal Amount);
// CancelledNote : renseigné quand le versement a été annulé APRÈS la clôture — il reste compté dans cette session
// (les chiffres signés ce jour-là ne bougent pas) mais le journal le dit.
public record TransactionRowDto(string Time, string ReceiptNumber, string StudentName, string Category, string Method, decimal Amount, string? CancelledNote = null);

// Une annulation comptée dans cette session : montant NÉGATIF, daté du moment de l'annulation.
public record ReversalRowDto(string Time, string ReceiptNumber, string StudentName, string Method, decimal Amount, string Note);
