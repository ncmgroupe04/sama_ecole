using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class WorkCertificatePdfGenerator : IWorkCertificatePdfGenerator
{
    static WorkCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(WorkCertificateDto certificate, byte[]? logo, byte[] qrCodeImage)
    {
        try
        {
            return new WorkCertificateDocument(certificate, logo, qrCodeImage).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new WorkCertificateDocument(certificate, null, qrCodeImage).GeneratePdf();
        }
    }
}
