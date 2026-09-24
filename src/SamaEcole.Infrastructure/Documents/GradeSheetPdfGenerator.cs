using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class GradeSheetPdfGenerator(ILogger<GradeSheetPdfGenerator>? logger = null) : IGradeSheetPdfGenerator
{
    static GradeSheetPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(GradeSheetPdfDto sheet, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"fiche de saisie des notes ({sheet.ClassroomName}, {sheet.SubjectName}, {sheet.EvaluationLabel}, {sheet.TermLabel})",
            () => new GradeSheetDocument(sheet, logo).GeneratePdf(),
            logo is not null ? () => new GradeSheetDocument(sheet, null).GeneratePdf() : null);
}
