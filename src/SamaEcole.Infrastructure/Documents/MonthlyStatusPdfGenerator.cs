using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

namespace SamaEcole.Infrastructure.Documents;

public class MonthlyStatusPdfGenerator(ILogger<MonthlyStatusPdfGenerator>? logger = null) : IMonthlyStatusPdfGenerator
{
    static MonthlyStatusPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(MonthlyStatusDto report, string schoolName, string? classroomName, string? status) =>
        PdfRenderGuard.Render(
            logger,
            $"situation des mensualités (école {schoolName}, mois {report.Month})",
            () => new MonthlyStatusDocument(report, schoolName, classroomName, status).GeneratePdf());
}
