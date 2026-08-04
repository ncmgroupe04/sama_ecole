using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Teachers;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="ITeachersExportPdfGenerator"/>. Sans état : un singleton
/// suffit, comme les autres générateurs PDF.
/// </summary>
public class TeachersExportPdfGenerator : ITeachersExportPdfGenerator
{
    static TeachersExportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(TeachersExportModel model)
        => new TeachersExportDocument(model).GeneratePdf();
}
