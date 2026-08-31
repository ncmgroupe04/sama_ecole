using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class WorkCertificatePdfGenerator(ILogger<WorkCertificatePdfGenerator>? logger = null) : IWorkCertificatePdfGenerator
{
    static WorkCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(WorkCertificateDto certificate, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"attestation de travail {certificate.CertificateNumber} ({certificate.EmployeeFullName}, contrat {certificate.ContractId})",
            () => new WorkCertificateDocument(certificate, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new WorkCertificateDocument(certificate, null, qrCodeImage).GeneratePdf() : null);
}
