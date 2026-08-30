using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

public class StateducReportPdfGenerator : IStateducReportPdfGenerator
{
    static StateducReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(
        StateducReportDto report,
        byte[]? directorSignature = null,
        byte[]? officialStamp = null) =>
        new StateducReportDocument(report, directorSignature, officialStamp).GeneratePdf();
}
