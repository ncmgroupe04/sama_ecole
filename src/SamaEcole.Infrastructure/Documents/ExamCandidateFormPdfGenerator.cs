using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExamCandidateFormPdfGenerator(IQrCodeService qrCodeService, ILogger<ExamCandidateFormPdfGenerator>? logger = null)
    : IExamCandidateFormPdfGenerator
{
    static ExamCandidateFormPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IReadOnlyList<ExamCandidateFormModel> candidates, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"fiche(s) de candidature examen ({candidates.Count} candidat(s), réf. {(candidates.Count > 0 ? candidates[0].Reference : "—")})",
            () => new ExamCandidateFormDocument(candidates, logo, qrCodeService.GenerateQrCode).GeneratePdf(),
            logo is not null
                ? () => new ExamCandidateFormDocument(candidates, null, qrCodeService.GenerateQrCode).GeneratePdf()
                : null);
}
