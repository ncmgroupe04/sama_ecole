using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

public class StudentMutationCertificatePdfGenerator : IStudentMutationCertificatePdfGenerator
{
    static StudentMutationCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(StudentMutationCertificateModel model, byte[]? qrCode) =>
        new StudentMutationCertificateDocument(model, qrCode).GeneratePdf();
}
