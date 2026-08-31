using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

public class StudentMutationCertificatePdfGenerator(ILogger<StudentMutationCertificatePdfGenerator>? logger = null)
    : IStudentMutationCertificatePdfGenerator
{
    static StudentMutationCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(StudentMutationCertificateModel model, byte[]? qrCode) =>
        PdfRenderGuard.Render(
            logger,
            $"certificat de mutation {model.CertificateNumber} (école {model.SchoolName})",
            () => new StudentMutationCertificateDocument(model, qrCode).GeneratePdf());
}
