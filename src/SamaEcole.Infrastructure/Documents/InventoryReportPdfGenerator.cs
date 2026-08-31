using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class InventoryReportPdfGenerator(ILogger<InventoryReportPdfGenerator>? logger = null) : IInventoryReportPdfGenerator
{
    static InventoryReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(InventoryReportModel model) =>
        PdfRenderGuard.Render(
            logger,
            $"fiche d'inventaire (école {model.SchoolName}, générée le {model.GeneratedOn:yyyy-MM-dd})",
            () => new InventoryReportDocument(model).GeneratePdf());
}
