using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat.Queries.GetStudentHizbReport;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class HizbReportPdfGenerator(ILogger<HizbReportPdfGenerator>? logger = null) : IHizbReportPdfGenerator
{
    static HizbReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(HizbReportDto report, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"bulletin coranique (matricule {report.Matricule}, élève {report.StudentId})",
            () => new HizbReportDocument(report, logo).GeneratePdf(),
            logo is not null ? () => new HizbReportDocument(report, null).GeneratePdf() : null);
}
