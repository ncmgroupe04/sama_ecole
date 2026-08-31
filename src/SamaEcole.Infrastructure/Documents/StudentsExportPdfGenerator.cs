using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IStudentsExportPdfGenerator"/>. Sans état : un singleton
/// suffit.
///
/// Pièce de travail interne (ni logo ni cachet, donc pas de passe dépouillée). <see cref="PdfRenderGuard"/>
/// garantit qu'un rendu vide LÈVE — le middleware d'exception en fait un 500 normalisé journalisé,
/// jamais un 200 au corps vide qui bloque l'aperçu client sur « document vide (0 octet) ».
/// </summary>
public class StudentsExportPdfGenerator(ILogger<StudentsExportPdfGenerator> logger) : IStudentsExportPdfGenerator
{
    static StudentsExportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(StudentsExportModel model) =>
        PdfRenderGuard.Render(
            logger,
            $"export PDF des élèves (école {model.SchoolName}, classe {model.ClassName ?? "toutes"}, {model.Students.Count} élève(s))",
            () => new StudentsExportDocument(model).GeneratePdf());
}
