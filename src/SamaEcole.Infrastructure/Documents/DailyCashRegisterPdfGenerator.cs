using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DailyCashRegisterPdfGenerator(ILogger<DailyCashRegisterPdfGenerator>? logger = null) : IDailyCashRegisterPdfGenerator
{
    static DailyCashRegisterPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DailyCashRegisterDto data, byte[]? schoolLogo) =>
        PdfRenderGuard.Render(
            logger,
            $"livre de caisse du jour (école {data.SchoolName}, {data.Date:yyyy-MM-dd})",
            () => new DailyCashRegisterDocument(data, schoolLogo).GeneratePdf(),
            schoolLogo is not null ? () => new DailyCashRegisterDocument(data, null).GeneratePdf() : null);
}
