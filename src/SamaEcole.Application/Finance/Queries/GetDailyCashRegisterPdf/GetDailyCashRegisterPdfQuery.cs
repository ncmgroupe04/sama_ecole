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
    decimal Amount
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

        var validPayments = await (
            from p in dbContext.Payments.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on p.EnrollmentId equals e.Id
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            where p.Status != PaymentStatus.Cancelled
               && p.PaidAt >= targetStart
               && p.PaidAt <= targetEnd
            orderby p.PaidAt descending
            select new DailyCashRegisterPaymentDto(
                p.PaidAt,
                s.FullName,
                s.Matricule,
                p.ReceiptNumber,
                p.Method.ToString(),
                p.Amount
            )
        ).ToListAsync(cancellationToken);

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
