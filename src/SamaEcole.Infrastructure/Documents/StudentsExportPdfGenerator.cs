using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IStudentsExportPdfGenerator"/>. Sans état : un singleton
/// suffit, comme les autres générateurs PDF.
/// </summary>
public class StudentsExportPdfGenerator : IStudentsExportPdfGenerator
{
    static StudentsExportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(StudentsExportModel model)
        => new StudentsExportDocument(model).GeneratePdf();
}
