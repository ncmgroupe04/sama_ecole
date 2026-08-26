using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExamCandidateFormPdfGenerator(IQrCodeService qrCodeService) : IExamCandidateFormPdfGenerator
{
    static ExamCandidateFormPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IReadOnlyList<ExamCandidateFormModel> candidates, byte[]? logo)
    {
        try
        {
            return new ExamCandidateFormDocument(candidates, logo, qrCodeService.GenerateQrCode).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            // Même repli que les autres pièces officielles : un logo illisible ou corrompu ne doit pas
            // empêcher l'émission du document, il doit seulement en disparaître.
            return new ExamCandidateFormDocument(candidates, null, qrCodeService.GenerateQrCode).GeneratePdf();
        }
    }
}
