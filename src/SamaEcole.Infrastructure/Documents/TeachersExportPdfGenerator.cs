using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Teachers;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="ITeachersExportPdfGenerator"/>. Pièce de travail interne
/// (ni logo ni cachet, donc pas de passe dépouillée) : <see cref="PdfRenderGuard"/> garantit
/// seulement qu'un rendu vide LÈVE une exception journalisée au lieu de renvoyer 0 octet.
/// </summary>
public class TeachersExportPdfGenerator(ILogger<TeachersExportPdfGenerator>? logger = null) : ITeachersExportPdfGenerator
{
    static TeachersExportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(TeachersExportModel model) =>
        PdfRenderGuard.Render(
            logger,
            $"export PDF des enseignants (école {model.SchoolName}, {model.Teachers.Count} enseignant(s))",
            () => new TeachersExportDocument(model).GeneratePdf());
}
