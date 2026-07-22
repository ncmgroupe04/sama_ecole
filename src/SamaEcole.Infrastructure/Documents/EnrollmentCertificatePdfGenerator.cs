using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class EnrollmentCertificatePdfGenerator : IEnrollmentCertificatePdfGenerator
{
    static EnrollmentCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EnrollmentCertificateDto certificate, byte[]? logo)
    {
        try
        {
            return new EnrollmentCertificateDocument(certificate, logo).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new EnrollmentCertificateDocument(certificate, null).GeneratePdf();
        }
    }
}
