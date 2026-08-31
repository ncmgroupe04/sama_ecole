using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExamConvocationPdfGenerator(ILogger<ExamConvocationPdfGenerator>? logger = null) : IExamConvocationPdfGenerator
{
    static ExamConvocationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExamConvocationModel model, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"convocation d'examen {model.Reference} (matricule {model.StudentMatricule}, n° table {model.CandidateNumber})",
            () => new ExamConvocationDocument(model, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new ExamConvocationDocument(model, null, qrCodeImage).GeneratePdf() : null);
}
