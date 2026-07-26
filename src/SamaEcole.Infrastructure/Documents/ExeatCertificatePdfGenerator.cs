using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExeatCertificatePdfGenerator : IExeatCertificatePdfGenerator
{
    static ExeatCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExeatCertificateDto certificate, byte[]? logo, byte[] qrCodeImage)
    {
        try
        {
            return new ExeatCertificateDocument(certificate, logo, qrCodeImage).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new ExeatCertificateDocument(certificate, null, qrCodeImage).GeneratePdf();
        }
    }
}
