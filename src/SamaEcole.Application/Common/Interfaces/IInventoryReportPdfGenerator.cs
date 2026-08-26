using SamaEcole.Application.Inventory;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la fiche d'inventaire global en PDF (GET /inventory/reports/global/pdf). Même convention que
/// les autres générateurs PDF (IStudentsExportPdfGenerator…) : moteur QuestPDF côté Infrastructure,
/// générateur pur et synchrone (les données sont déjà résolues dans <see cref="InventoryReportModel"/>).
/// </summary>
public interface IInventoryReportPdfGenerator
{
    byte[] Generate(InventoryReportModel model);
}
