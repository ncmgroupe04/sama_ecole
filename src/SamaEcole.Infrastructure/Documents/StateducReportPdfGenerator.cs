using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

public class StateducReportPdfGenerator(ILogger<StateducReportPdfGenerator>? logger = null) : IStateducReportPdfGenerator
{
    static StateducReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(
        StateducReportDto report,
        byte[]? directorSignature = null,
        byte[]? officialStamp = null) =>
        PdfRenderGuard.Render(
            logger,
            $"rapport STATEDUC (école {report.SchoolName}, code {report.NationalSchoolCode ?? "—"})",
            () => new StateducReportDocument(report, directorSignature, officialStamp).GeneratePdf(),
            directorSignature is not null || officialStamp is not null
                ? () => new StateducReportDocument(report, null, null).GeneratePdf()
                : null);
}
