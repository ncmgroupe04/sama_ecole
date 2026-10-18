using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;

public record GetDailyCashRegisterPdfQuery(DateOnly Date) : IRequest<DailyCashRegisterPdfResult>, IAuditableRequest;

public record DailyCashRegisterPdfResult(byte[] Content, DateOnly Date);

public record DailyCashRegisterDto(
    string SchoolName,
    string? SchoolAddress,
    string? SchoolEmail,
    string? SchoolPhone,
    string? SchoolLogoUrl,
    DateOnly Date,
    decimal TotalCollected,
    IReadOnlyList<DailyCashRegisterMethodTotalDto> TotalsByMethod,
    IReadOnlyList<DailyCashRegisterPaymentDto> Payments
);

public record DailyCashRegisterMethodTotalDto(
    string Method,
    decimal Total
);

public record DailyCashRegisterPaymentDto(
    DateTimeOffset PaidAt,
    string StudentFullName,
    string Matricule,
    string ReceiptNumber,
    string Method,
    decimal Amount,
    // Annulation d'un versement d'un jour ANTÉRIEUR : montant négatif, daté de l'annulation (PaymentLedger).
    bool IsReversal = false,
    // Mention imprimée sous la ligne : « Annulé le … » sur un versement annulé depuis, ou le détail d'une annulation.
    string? Note = null
);

public interface IDailyCashRegisterPdfGenerator
{
    byte[] Generate(DailyCashRegisterDto data, byte[]? schoolLogo);
}

public class GetDailyCashRegisterPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IDailyCashRegisterPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetDailyCashRegisterPdfQuery, DailyCashRegisterPdfResult>
{
    public async Task<DailyCashRegisterPdfResult> Handle(GetDailyCashRegisterPdfQuery request, CancellationToken cancellationToken)
    {
        var currentSchoolId = tenantProvider.CurrentSchoolId;

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == currentSchoolId, cancellationToken)
            ?? throw new KeyNotFoundException("L'école courante n'a pas été trouvée.");

        // Using UTC bounds since the database stores dates in UTC.
        // It's a simplification. A better approach would be to use TimeProvider's Local timezone offset.
        // Let's use UtcNow for bound alignment, but Wait, we just want the day.
        var targetStart = new DateTimeOffset(request.Date.Year, request.Date.Month, request.Date.Day, 0, 0, 0, TimeSpan.Zero);
        var targetEnd = targetStart.AddDays(1).AddTicks(-1);

        // Registre daté (PaymentLedger) : les versements du jour — y compris ceux annulés un autre jour, signalés « annulé
        // le … » sans être retirés (le journal d'hier reste celui d'hier) — et, en négatif, les annulations faites CE
        // jour pour des versements d'un jour antérieur. Un versement annulé le jour même disparaît (l'erreur et sa
        // correction se compensent).
        var entries = await PaymentLedger.EntriesAsync(dbContext, targetStart, targetStart.AddDays(1), cancellationToken);

        var enrollmentIds = entries.Select(e => e.EnrollmentId).Distinct().ToList();
        var students = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            where enrollmentIds.Contains(e.Id)
            select new { EnrollmentId = e.Id, s.FullName, s.Matricule })
            .ToDictionaryAsync(x => x.EnrollmentId, cancellationToken);

        var validPayments = entries
            .OrderByDescending(e => e.At)
            .Select(e =>
            {
                var student = students.GetValueOrDefault(e.EnrollmentId);
                var note = e.IsReversal
                    ? $"Annulation du reçu encaissé le {e.PaidAt:dd/MM/yyyy} — "
                      + $"{(string.IsNullOrWhiteSpace(e.CancellationReason) ? "sans motif" : e.CancellationReason)} "
                      + $"({PaymentCancellationOutcomeLabels.Label(e.Outcome)})"
                    : e.CancelledLater
                        ? PaymentCancellationOutcomeLabels.CancelledNote(e.CancelledAt, e.CancellationReason, e.Outcome)
                        : null;

                return new DailyCashRegisterPaymentDto(
                    e.At, student?.FullName ?? "Inconnu", student?.Matricule ?? "—",
                    e.ReceiptNumber, e.Method.ToString(), e.Amount, e.IsReversal, note);
            })
            .ToList();

        // Montants SIGNÉS : le total et le récapitulatif par mode sont NETS des annulations.
        var totalCollected = validPayments.Sum(p => p.Amount);

        var totalsByMethod = validPayments
            .GroupBy(p => p.Method)
            .Select(g => new DailyCashRegisterMethodTotalDto(g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Total)
            .ToList();

        var data = new DailyCashRegisterDto(
            school.Name,
            school.Address,
            school.Email,
            school.Phone,
            school.LogoUrl,
            request.Date,
            totalCollected,
            totalsByMethod,
            validPayments
        );

        var logo = await logoProvider.TryFetchAsync(data.SchoolLogoUrl, cancellationToken);

        var content = pdfGenerator.Generate(data, logo);

        return new DailyCashRegisterPdfResult(content, request.Date);
    }
}
