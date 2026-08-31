using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

public class SkillsBookletPdfGenerator(ILogger<SkillsBookletPdfGenerator>? logger = null) : ISkillsBookletPdfGenerator
{
    static SkillsBookletPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(SkillsBookletModel model) =>
        PdfRenderGuard.Render(
            logger,
            $"livret de compétences (matricule {model.Matricule}, {model.StudentFullName})",
            () => new SkillsBookletDocument(model).GeneratePdf());
}
