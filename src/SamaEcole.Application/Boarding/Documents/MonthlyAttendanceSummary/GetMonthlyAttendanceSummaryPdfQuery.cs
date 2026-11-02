using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Attendance.Sessions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Documents.MonthlyAttendanceSummary;

/// <summary>
/// Données d'impression du récapitulatif mensuel d'une session. Document de gestion (pension, repas, nuits effectives) : le nom, le
/// matricule et les comptes — AUCUNE donnée de santé ni téléphone.
/// </summary>
public record MonthlyAttendanceSummaryReportDto(
    string SchoolName,
    string? SchoolAddress,
    string? SchoolLogoUrl,
    string? SchoolYearLabel,
    MonthlySummaryDto Summary,
    DateTimeOffset GeneratedAt);

/// <summary>
/// GET /api/v1/boarding/attendance/monthly-summary/pdf?month=YYYY-MM&amp;sessionId=&amp;groupId= — « Récapitulatif mensuel du pointage »,
/// imprimable. Mêmes comptes, mêmes droits et mêmes portées que la lecture JSON. Action tracée comme tout document imprimé.
/// </summary>
public record GetMonthlyAttendanceSummaryPdfQuery(DateOnly MonthStart, Guid SessionId, Guid? GroupId)
    : IRequest<BoardingPdfResult>, IAuditableRequest;

public class GetMonthlyAttendanceSummaryPdfQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider,
    IMonthlyAttendanceSummaryPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetMonthlyAttendanceSummaryPdfQuery, BoardingPdfResult>
{
    public async Task<BoardingPdfResult> Handle(GetMonthlyAttendanceSummaryPdfQuery request, CancellationToken cancellationToken)
    {
        // 404 (session ou groupe), 403 (Halqa d'un autre) et 422 (mois invalide) sont levés ici.
        var summary = await mediator.Send(
            new GetMonthlyAttendanceSummaryQuery(request.MonthStart, request.SessionId, request.GroupId), cancellationToken);

        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new { s.Name, s.Address, s.LogoUrl })
            .FirstAsync(cancellationToken);

        var yearLabel = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken);

        var report = new MonthlyAttendanceSummaryReportDto(
            school.Name, school.Address, school.LogoUrl, yearLabel, summary, timeProvider.GetUtcNow());
        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);

        return new BoardingPdfResult(
            pdfGenerator.Generate(report, logo), $"{summary.SessionName}-{summary.GroupName}-{summary.MonthStart:yyyy-MM}");
    }
}
