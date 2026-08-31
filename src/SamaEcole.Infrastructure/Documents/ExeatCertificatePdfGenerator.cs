using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExeatCertificatePdfGenerator(ILogger<ExeatCertificatePdfGenerator>? logger = null) : IExeatCertificatePdfGenerator
{
    static ExeatCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExeatCertificateDto certificate, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"certificat d'exéat {certificate.CertificateNumber} (matricule {certificate.Matricule}, inscription {certificate.EnrollmentId})",
            () => new ExeatCertificateDocument(certificate, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new ExeatCertificateDocument(certificate, null, qrCodeImage).GeneratePdf() : null);
}
