using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Reports.Queries.GetAttendanceExport;

public class GetAttendanceExportQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    AttendanceReportAggregator aggregator,
    IAttendanceReportPdfGenerator pdfGenerator)
    : IRequestHandler<GetAttendanceExportQuery, AttendanceExportResult>
{
    public async Task<AttendanceExportResult> Handle(GetAttendanceExportQuery request, CancellationToken cancellationToken)
    {
        // L'agrégateur valide le classId (⊂ tenant) et calcule sous la RLS : les données du fichier ne
        // franchissent jamais la frontière d'une autre école (exigence R03).
        var aggregate = await aggregator.ComputeAsync(request.StartDate, request.EndDate, request.ClassId, cancellationToken);

        var schoolName = string.Empty;
        if (tenantProvider.CurrentSchoolId is { } schoolId)
        {
            schoolName = await dbContext.Schools.AsNoTracking()
                .Where(s => s.Id == schoolId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        string? className = null;
        if (request.ClassId is { } classId)
        {
            className = await dbContext.Classrooms.AsNoTracking()
                .Where(c => c.Id == classId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var model = new AttendanceReportExportModel(
            schoolName, request.StartDate, request.EndDate, className, aggregate.AverageAttendanceRate, aggregate.Students);

        var datePart = $"{request.StartDate:yyyyMMdd}-{request.EndDate:yyyyMMdd}";

        return request.Format.Trim().ToLowerInvariant() switch
        {
            "csv" => new AttendanceExportResult(
                AttendanceReportCsv.Build(model), $"assiduite_{datePart}.csv", "text/csv"),
            _ => new AttendanceExportResult(
                pdfGenerator.Generate(model), $"assiduite_{datePart}.pdf", "application/pdf")
        };
    }
}
