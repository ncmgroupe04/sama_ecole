using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Boarding.Documents.DormitoryRegister;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Documents;

public class DormitoryRegisterPdfGenerator(ILogger<DormitoryRegisterPdfGenerator>? logger = null) : IDormitoryRegisterPdfGenerator
{
    static DormitoryRegisterPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DormitoryRegisterDto register, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"registre du pavillon {register.DormitoryName} ({register.Date:yyyy-MM-dd})",
            () => new DormitoryRegisterDocument(register, logo).GeneratePdf(),
            logo is not null ? () => new DormitoryRegisterDocument(register, null).GeneratePdf() : null);
}
