using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class EnrollmentCertificatePdfGenerator(ILogger<EnrollmentCertificatePdfGenerator>? logger = null) : IEnrollmentCertificatePdfGenerator
{
    static EnrollmentCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EnrollmentCertificateDto certificate, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"certificat de scolarité {certificate.CertificateNumber} (matricule {certificate.Matricule}, inscription {certificate.EnrollmentId})",
            () => new EnrollmentCertificateDocument(certificate, logo).GeneratePdf(),
            logo is not null ? () => new EnrollmentCertificateDocument(certificate, null).GeneratePdf() : null);
}
