using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class PayslipPdfGenerator : IPayslipPdfGenerator
{
    static PayslipPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PayslipDto payslip, byte[]? logo)
    {
        try
        {
            return new PayslipDocument(payslip, logo).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new PayslipDocument(payslip, null).GeneratePdf();
        }
    }
}
