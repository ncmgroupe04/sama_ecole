using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class HourRecordSheetPdfGenerator(ILogger<HourRecordSheetPdfGenerator>? logger = null) : IHourRecordSheetPdfGenerator
{
    static HourRecordSheetPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(HourRecordSheetDto sheet, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"fiche des heures {sheet.SheetNumber} ({sheet.EmployeeFullName}, {sheet.Month:00}/{sheet.Year}, contrat {sheet.EmployeeContractId})",
            () => new HourRecordSheetDocument(sheet, logo).GeneratePdf(),
            logo is not null ? () => new HourRecordSheetDocument(sheet, null).GeneratePdf() : null);
}
