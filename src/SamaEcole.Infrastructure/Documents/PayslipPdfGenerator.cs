using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class PayslipPdfGenerator(ILogger<PayslipPdfGenerator>? logger = null) : IPayslipPdfGenerator
{
    static PayslipPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PayslipDto payslip, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"bulletin de paie {payslip.PayslipNumber} ({payslip.EmployeeFullName}, fiche {payslip.FichePaieId})",
            () => new PayslipDocument(payslip, logo).GeneratePdf(),
            logo is not null ? () => new PayslipDocument(payslip, null).GeneratePdf() : null);
}
