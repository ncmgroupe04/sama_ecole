using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExamConvocationPdfGenerator : IExamConvocationPdfGenerator
{
    static ExamConvocationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExamConvocationModel model, byte[]? logo, byte[] qrCodeImage)
    {
        try
        {
            return new ExamConvocationDocument(model, logo, qrCodeImage).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new ExamConvocationDocument(model, null, qrCodeImage).GeneratePdf();
        }
    }
}
