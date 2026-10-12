using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExamSynthesisPdfGenerator(ILogger<ExamSynthesisPdfGenerator>? logger = null) : IExamSynthesisPdfGenerator
{
    static ExamSynthesisPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateSessionPv(ExamSynthesisModel model, byte[]? logo) =>
        Render(model, logo, sessionPv: true, $"PV de session d'examen {model.Reference} ({model.Candidates.Count} candidat(s))");

    public byte[] GenerateCandidateSheet(ExamSynthesisModel model, byte[]? logo) =>
        Render(model, logo, sessionPv: false, $"fiche synthétique d'examen {model.Reference} ({model.Candidates.Count} candidat(s))");

    private byte[] Render(ExamSynthesisModel model, byte[]? logo, bool sessionPv, string label) =>
        PdfRenderGuard.Render(
            logger,
            label,
            () => new ExamSynthesisDocument(model, logo, sessionPv).GeneratePdf(),
            logo is not null ? () => new ExamSynthesisDocument(model, null, sessionPv).GeneratePdf() : null);
}
