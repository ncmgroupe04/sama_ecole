using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DailyCashRegisterPdfGenerator : IDailyCashRegisterPdfGenerator
{
    public byte[] Generate(DailyCashRegisterDto data, byte[]? schoolLogo)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var document = new DailyCashRegisterDocument(data, schoolLogo);
        return document.GeneratePdf();
    }
}
