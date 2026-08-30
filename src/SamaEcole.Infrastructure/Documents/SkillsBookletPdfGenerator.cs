using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

public class SkillsBookletPdfGenerator : ISkillsBookletPdfGenerator
{
    static SkillsBookletPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(SkillsBookletModel model) => new SkillsBookletDocument(model).GeneratePdf();
}
