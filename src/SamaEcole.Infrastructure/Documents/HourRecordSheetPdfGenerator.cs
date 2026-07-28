using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class HourRecordSheetPdfGenerator : IHourRecordSheetPdfGenerator
{
    static HourRecordSheetPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(HourRecordSheetDto sheet, byte[]? logo)
    {
        try
        {
            return new HourRecordSheetDocument(sheet, logo).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new HourRecordSheetDocument(sheet, null).GeneratePdf();
        }
    }
}
