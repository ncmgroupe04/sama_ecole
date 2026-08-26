using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class InventoryReportPdfGenerator : IInventoryReportPdfGenerator
{
    static InventoryReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(InventoryReportModel model) => new InventoryReportDocument(model).GeneratePdf();
}
