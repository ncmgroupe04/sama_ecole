using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Boarding.Documents.MonthlyAttendanceSummary;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Documents;

public class MonthlyAttendanceSummaryPdfGenerator(ILogger<MonthlyAttendanceSummaryPdfGenerator>? logger = null)
    : IMonthlyAttendanceSummaryPdfGenerator
{
    static MonthlyAttendanceSummaryPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(MonthlyAttendanceSummaryReportDto report, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"récapitulatif mensuel {report.Summary.SessionName} ({report.Summary.MonthStart:yyyy-MM})",
            () => new MonthlyAttendanceSummaryDocument(report, logo).GeneratePdf(),
            logo is not null ? () => new MonthlyAttendanceSummaryDocument(report, null).GeneratePdf() : null);
}
