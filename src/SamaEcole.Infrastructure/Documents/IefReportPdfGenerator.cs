using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Institutional;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>Implémentation QuestPDF du rapport de rentrée IEF, via <see cref="PdfRenderGuard"/> (jamais 0 octet).</summary>
public class IefReportPdfGenerator(ILogger<IefReportPdfGenerator>? logger = null) : IIefReportPdfGenerator
{
    static IefReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IefReportDto report) =>
        PdfRenderGuard.Render(
            logger,
            $"rapport de rentrée IEF (école {report.SchoolName}, {report.SchoolYearLabel})",
            () => new IefReportDocument(report).GeneratePdf(),
            null);
}
