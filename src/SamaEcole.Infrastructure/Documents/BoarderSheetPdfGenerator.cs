using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Boarding.Documents.BoarderSheet;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Documents;

public class BoarderSheetPdfGenerator(ILogger<BoarderSheetPdfGenerator>? logger = null) : IBoarderSheetPdfGenerator
{
    static BoarderSheetPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // Le libellé de journal ne porte que le matricule : jamais de donnée de santé dans un journal d'erreur.
    public byte[] Generate(BoarderSheetDto sheet, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"fiche pensionnaire (matricule {sheet.Boarder.Matricule})",
            () => new BoarderSheetDocument(sheet, logo).GeneratePdf(),
            logo is not null ? () => new BoarderSheetDocument(sheet, null).GeneratePdf() : null);
}
